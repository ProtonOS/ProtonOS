// ProtonOS korlib - Runtime Reflection Types
// Concrete implementations of reflection types that bridge to kernel reflection APIs.

using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace System.Reflection
{
    /// <summary>
    /// Concrete implementation of MethodInfo for runtime methods.
    /// </summary>
    public sealed unsafe class RuntimeMethodInfo : MethodInfo
    {
        private readonly uint _assemblyId;
        private readonly uint _methodToken;
        private readonly Type _declaringType;
        private string? _name;
        private bool _isStatic;
        private bool _isStaticChecked;

        internal RuntimeMethodInfo(uint assemblyId, uint methodToken, Type declaringType)
        {
            _assemblyId = assemblyId;
            _methodToken = methodToken;
            _declaringType = declaringType;
        }

        public override string Name
        {
            get
            {
                if (_name == null)
                {
                    byte* namePtr = PalGetMethodName(_assemblyId, _methodToken);
                    if (namePtr != null)
                        _name = BytePtrToString(namePtr);
                    else
                        _name = "<unknown>";
                }
                return _name;
            }
        }

        public override Type? DeclaringType => _declaringType;

        public override MemberTypes MemberType => MemberTypes.Method;

        public override RuntimeMethodHandle MethodHandle =>
            new RuntimeMethodHandle((nint)_methodToken);

        public override MethodAttributes Attributes
        {
            get
            {
                MethodAttributes attrs = MethodAttributes.Public;
                if (IsStatic)
                    attrs |= MethodAttributes.Static;
                if (IsVirtual)
                    attrs |= MethodAttributes.Virtual;
                return attrs;
            }
        }

        public bool IsStatic
        {
            get
            {
                if (!_isStaticChecked)
                {
                    _isStatic = PalIsMethodStatic(_assemblyId, _methodToken);
                    _isStaticChecked = true;
                }
                return _isStatic;
            }
        }

        public bool IsVirtual => PalIsMethodVirtual(_assemblyId, _methodToken);

        public override ParameterInfo[] GetParameters()
        {
            // Simplified - return empty for now
            return new ParameterInfo[0];
        }

        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
        {
            return PalInvokeMethod(_methodToken, obj, parameters);
        }

        public override MethodInfo GetBaseDefinition() => this;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes =>
            throw new NotSupportedException();

        public override int MetadataToken => (int)_methodToken;

        // Import kernel reflection APIs
        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern byte* PalGetMethodName(uint assemblyId, uint methodToken);

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool PalIsMethodStatic(uint assemblyId, uint methodToken);

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool PalIsMethodVirtual(uint assemblyId, uint methodToken);

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern object? PalInvokeMethod(uint methodToken, object? target, object?[]? args);

        private static string BytePtrToString(byte* ptr)
        {
            if (ptr == null)
                return string.Empty;

            int len = 0;
            while (ptr[len] != 0)
                len++;

            if (len == 0)
                return string.Empty;

            char* chars = stackalloc char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)ptr[i];

            return new string(chars, 0, len);
        }
    }

    /// <summary>
    /// Concrete implementation of FieldInfo for runtime fields.
    /// </summary>
    public sealed unsafe class RuntimeFieldInfo : FieldInfo
    {
        private readonly uint _assemblyId;
        private readonly uint _fieldToken;
        private readonly Type _declaringType;
        private readonly int _fieldOffset;
        private readonly int _fieldSize;
        private readonly bool _isValueType;
        private string? _name;

        internal RuntimeFieldInfo(uint assemblyId, uint fieldToken, Type declaringType,
            int fieldOffset, int fieldSize, bool isValueType)
        {
            _assemblyId = assemblyId;
            _fieldToken = fieldToken;
            _declaringType = declaringType;
            _fieldOffset = fieldOffset;
            _fieldSize = fieldSize;
            _isValueType = isValueType;
        }

        public override string Name
        {
            get
            {
                if (_name == null)
                {
                    byte* namePtr = PalGetFieldName(_assemblyId, _fieldToken);
                    if (namePtr != null)
                        _name = BytePtrToString(namePtr);
                    else
                        _name = "<unknown>";
                }
                return _name;
            }
        }

        public override Type? DeclaringType => _declaringType;

        public override MemberTypes MemberType => MemberTypes.Field;

        public override RuntimeFieldHandle FieldHandle =>
            new RuntimeFieldHandle((nint)_fieldToken);

        public override Type FieldType => null!; // Simplified - no typeof() without reflection

        public override FieldAttributes Attributes => FieldAttributes.Public;

        public override object? GetValue(object? obj)
        {
            return PalGetFieldValue(_fieldToken, obj, _fieldOffset, _fieldSize, _isValueType);
        }

        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr,
            Binder? binder, CultureInfo? culture)
        {
            PalSetFieldValue(_fieldToken, obj, _fieldOffset, _fieldSize, _isValueType, value);
        }

        public override int MetadataToken => (int)_fieldToken;

        // Import kernel reflection APIs
        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern byte* PalGetFieldName(uint assemblyId, uint fieldToken);

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern object? PalGetFieldValue(uint fieldToken, object? target,
            int fieldOffset, int fieldSize, bool isValueType);

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void PalSetFieldValue(uint fieldToken, object? target,
            int fieldOffset, int fieldSize, bool isValueType, object? value);

        private static string BytePtrToString(byte* ptr)
        {
            if (ptr == null)
                return string.Empty;

            int len = 0;
            while (ptr[len] != 0)
                len++;

            if (len == 0)
                return string.Empty;

            char* chars = stackalloc char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)ptr[i];

            return new string(chars, 0, len);
        }
    }

    /// <summary>
    /// Concrete implementation of PropertyInfo for runtime properties.
    /// </summary>
    public sealed class RuntimePropertyInfo : PropertyInfo
    {
        private readonly Type _declaringType;
        private readonly string _name;
        private readonly MethodInfo? _getter;
        private readonly MethodInfo? _setter;

        internal RuntimePropertyInfo(Type declaringType, string name,
            MethodInfo? getter, MethodInfo? setter)
        {
            _declaringType = declaringType;
            _name = name;
            _getter = getter;
            _setter = setter;
        }

        public override string Name => _name;

        public override Type? DeclaringType => _declaringType;

        public override MemberTypes MemberType => MemberTypes.Property;

        public override Type PropertyType => null!; // Simplified - no typeof() without reflection

        public override PropertyAttributes Attributes => PropertyAttributes.None;

        public override bool CanRead => _getter != null;

        public override bool CanWrite => _setter != null;

        public override MethodInfo? GetGetMethod(bool nonPublic) => _getter;

        public override MethodInfo? GetSetMethod(bool nonPublic) => _setter;

        public override ParameterInfo[] GetIndexParameters() => new ParameterInfo[0];

        public override object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? index, System.Globalization.CultureInfo? culture)
        {
            if (_getter == null)
                throw new InvalidOperationException("Property has no getter");
            return _getter.Invoke(obj, index);
        }

        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr,
            Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture)
        {
            if (_setter == null)
                throw new InvalidOperationException("Property has no setter");

            object?[]? args;
            if (index == null || index.Length == 0)
                args = new object?[] { value };
            else
            {
                args = new object?[index.Length + 1];
                for (int i = 0; i < index.Length; i++)
                    args[i] = index[i];
                args[index.Length] = value;
            }
            _setter.Invoke(obj, args);
        }

        public override MethodInfo[] GetAccessors(bool nonPublic)
        {
            int count = (_getter != null ? 1 : 0) + (_setter != null ? 1 : 0);
            var accessors = new MethodInfo[count];
            int idx = 0;
            if (_getter != null)
                accessors[idx++] = _getter;
            if (_setter != null)
                accessors[idx++] = _setter;
            return accessors;
        }
    }

    /// <summary>
    /// Concrete implementation of ConstructorInfo for runtime constructors.
    /// </summary>
    public sealed unsafe class RuntimeConstructorInfo : ConstructorInfo
    {
        private readonly uint _assemblyId;
        private readonly uint _methodToken;
        private readonly Type _declaringType;
        private string? _name;

        internal RuntimeConstructorInfo(uint assemblyId, uint methodToken, Type declaringType)
        {
            _assemblyId = assemblyId;
            _methodToken = methodToken;
            _declaringType = declaringType;
        }

        public override string Name
        {
            get
            {
                if (_name == null)
                {
                    byte* namePtr = PalGetMethodName(_assemblyId, _methodToken);
                    if (namePtr != null)
                        _name = BytePtrToString(namePtr);
                    else
                        _name = ".ctor";
                }
                return _name;
            }
        }

        public override Type? DeclaringType => _declaringType;

        public override MemberTypes MemberType => MemberTypes.Constructor;

        public override RuntimeMethodHandle MethodHandle =>
            new RuntimeMethodHandle((nint)_methodToken);

        public override MethodAttributes Attributes => MethodAttributes.Public;

        public override ParameterInfo[] GetParameters() => new ParameterInfo[0];

        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
        {
            return PalInvokeMethod(_methodToken, obj, parameters);
        }

        public override object Invoke(BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
        {
            // Create instance then call constructor
            // For now, simplified - just invoke
            return PalInvokeMethod(_methodToken, null, parameters) ?? new object();
        }

        public override int MetadataToken => (int)_methodToken;

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern byte* PalGetMethodName(uint assemblyId, uint methodToken);

        [DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern object? PalInvokeMethod(uint methodToken, object? target, object?[]? args);

        private static string BytePtrToString(byte* ptr)
        {
            if (ptr == null)
                return string.Empty;

            int len = 0;
            while (ptr[len] != 0)
                len++;

            if (len == 0)
                return string.Empty;

            char* chars = stackalloc char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)ptr[i];

            return new string(chars, 0, len);
        }
    }
}
