// ProtonOS korlib - Runtime Reflection Types
// Concrete implementations of reflection types that bridge to kernel reflection APIs.

using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace System.Reflection
{
    /// <summary>
    /// Concrete implementation of ParameterInfo for runtime method parameters.
    /// </summary>
    public sealed unsafe class RuntimeParameterInfo : ParameterInfo
    {
        private readonly uint _assemblyId;
        private readonly uint _methodToken;
        private readonly int _position;
        private string? _name;
        private Type? _parameterType;

        internal RuntimeParameterInfo(uint assemblyId, uint methodToken, int position)
        {
            _assemblyId = assemblyId;
            _methodToken = methodToken;
            _position = position;
        }

        public override string? Name
        {
            get
            {
                if (_name == null)
                {
                    byte* namePtr = Reflection_GetMethodParameterName(_assemblyId, _methodToken, _position);
                    if (namePtr != null)
                        _name = BytePtrToString(namePtr);
                }
                return _name;
            }
        }

        public override Type ParameterType
        {
            get
            {
                if (_parameterType == null)
                {
                    void* mt = Reflection_GetMethodParameterTypeMethodTable(_assemblyId, _methodToken, _position);
                    if (mt != null)
                        _parameterType = Type.GetTypeFromHandle(new RuntimeTypeHandle((nint)mt));
                }
                return _parameterType!;
            }
        }

        public override int Position => _position;

        // Internal accessors for AOT helper bypass
        internal uint AssemblyId => _assemblyId;
        internal uint MethodToken => _methodToken;

        [DllImport("*", EntryPoint = "Reflection_GetMethodParameterName", CallingConvention = CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetMethodParameterName(uint assemblyId, uint methodToken, int paramIndex);

        [DllImport("*", EntryPoint = "Reflection_GetMethodParameterTypeMethodTable", CallingConvention = CallingConvention.Cdecl)]
        private static extern void* Reflection_GetMethodParameterTypeMethodTable(uint assemblyId, uint methodToken, int paramIndex);

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
                    byte* namePtr = Reflection_GetMethodName(_assemblyId, _methodToken);
                    if (namePtr != null)
                        _name = BytePtrToString(namePtr);
                    else
                        _name = "<unknown>";

                    // Debug: print token and cached name length
                    byte* prefix = stackalloc byte[20];
                    prefix[0] = (byte)'N'; prefix[1] = (byte)'a'; prefix[2] = (byte)'m'; prefix[3] = (byte)'e';
                    prefix[4] = (byte)' '; prefix[5] = (byte)'c'; prefix[6] = (byte)'a'; prefix[7] = (byte)'c';
                    prefix[8] = (byte)'h'; prefix[9] = (byte)'e'; prefix[10] = (byte)'d'; prefix[11] = (byte)'=';
                    prefix[12] = 0;
                    Debug_PrintInt(prefix, _name.Length);
                }
                else
                {
                    // Debug: print when returning cached value
                    byte* prefix = stackalloc byte[20];
                    prefix[0] = (byte)'N'; prefix[1] = (byte)'a'; prefix[2] = (byte)'m'; prefix[3] = (byte)'e';
                    prefix[4] = (byte)' '; prefix[5] = (byte)'r'; prefix[6] = (byte)'e'; prefix[7] = (byte)'t';
                    prefix[8] = (byte)'u'; prefix[9] = (byte)'r'; prefix[10] = (byte)'n'; prefix[11] = (byte)'=';
                    prefix[12] = 0;
                    Debug_PrintInt(prefix, _name.Length);
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
                    _isStatic = Reflection_IsMethodStatic(_assemblyId, _methodToken);
                    _isStaticChecked = true;
                }
                return _isStatic;
            }
        }

        public bool IsVirtual => Reflection_IsMethodVirtual(_assemblyId, _methodToken);

        public override ParameterInfo[] GetParameters()
        {
            int count = Reflection_GetMethodParameterCount(_assemblyId, _methodToken);
            if (count <= 0)
                return new ParameterInfo[0];

            var parameters = new ParameterInfo[count];
            for (int i = 0; i < count; i++)
            {
                parameters[i] = new RuntimeParameterInfo(_assemblyId, _methodToken, i);
            }
            return parameters;
        }

        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
        {
            return Reflection_InvokeMethod(_assemblyId, _methodToken, obj, parameters);
        }

        public override MethodInfo GetBaseDefinition() => this;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes =>
            throw new NotSupportedException();

        public override int MetadataToken => (int)_methodToken;

        /// <summary>
        /// Gets the assembly ID where this method is defined.
        /// ProtonOS-specific extension for reflection invoke support.
        /// </summary>
        internal uint AssemblyId => _assemblyId;

        // Import kernel reflection APIs
        [DllImport("*", EntryPoint = "Reflection_GetMethodParameterCount", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int Reflection_GetMethodParameterCount(uint assemblyId, uint methodToken);

        [DllImport("*", EntryPoint = "Reflection_GetMethodName", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetMethodName(uint assemblyId, uint methodToken);

        [DllImport("*", EntryPoint = "Reflection_IsMethodStatic", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool Reflection_IsMethodStatic(uint assemblyId, uint methodToken);

        [DllImport("*", EntryPoint = "Reflection_IsMethodVirtual", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern bool Reflection_IsMethodVirtual(uint assemblyId, uint methodToken);

        [DllImport("*", EntryPoint = "Reflection_InvokeMethod", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern object? Reflection_InvokeMethod(uint assemblyId, uint methodToken, object? target, object?[]? args);

        [DllImport("*", EntryPoint = "Debug_PrintInt", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void Debug_PrintInt(byte* prefix, int value);

        private static string BytePtrToString(byte* ptr)
        {
            if (ptr == null)
                return string.Empty;

            int len = 0;
            while (ptr[len] != 0)
                len++;

            if (len == 0)
                return string.Empty;

            // Debug: print calculated length
            byte* prefixLen = stackalloc byte[16];
            prefixLen[0] = (byte)'M'; prefixLen[1] = (byte)'e'; prefixLen[2] = (byte)'t'; prefixLen[3] = (byte)'h';
            prefixLen[4] = (byte)' '; prefixLen[5] = (byte)'l'; prefixLen[6] = (byte)'e'; prefixLen[7] = (byte)'n';
            prefixLen[8] = (byte)'='; prefixLen[9] = 0;
            Debug_PrintInt(prefixLen, len);

            char* chars = stackalloc char[len];
            for (int i = 0; i < len; i++)
                chars[i] = (char)ptr[i];

            string result = new string(chars, 0, len);

            // Debug: print resulting string length
            byte* prefixRes = stackalloc byte[16];
            prefixRes[0] = (byte)'M'; prefixRes[1] = (byte)'e'; prefixRes[2] = (byte)'t'; prefixRes[3] = (byte)'h';
            prefixRes[4] = (byte)' '; prefixRes[5] = (byte)'r'; prefixRes[6] = (byte)'e'; prefixRes[7] = (byte)'s';
            prefixRes[8] = (byte)'='; prefixRes[9] = 0;
            Debug_PrintInt(prefixRes, result.Length);

            return result;
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
        private Type? _fieldType;

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
                    byte* namePtr = Reflection_GetFieldName(_assemblyId, _fieldToken);
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

        public override Type FieldType
        {
            get
            {
                if (_fieldType == null)
                {
                    void* mt = Reflection_GetFieldTypeMethodTable(_assemblyId, _fieldToken);
                    if (mt != null)
                        _fieldType = Type.GetTypeFromHandle(new RuntimeTypeHandle((nint)mt));
                }
                return _fieldType!;
            }
        }

        [DllImport("*", EntryPoint = "Reflection_GetFieldTypeMethodTable", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void* Reflection_GetFieldTypeMethodTable(uint assemblyId, uint fieldToken);

        public override FieldAttributes Attributes => FieldAttributes.Public;

        public override object? GetValue(object? obj)
        {
            return Reflection_GetFieldValue(_fieldToken, obj, _fieldOffset, _fieldSize, _isValueType);
        }

        public override void SetValue(object? obj, object? value, BindingFlags invokeAttr,
            Binder? binder, CultureInfo? culture)
        {
            Reflection_SetFieldValue(_fieldToken, obj, _fieldOffset, _fieldSize, _isValueType, value);
        }

        public override int MetadataToken => (int)_fieldToken;

        // Import kernel reflection APIs
        [DllImport("*", EntryPoint = "Reflection_GetFieldName", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetFieldName(uint assemblyId, uint fieldToken);

        [DllImport("*", EntryPoint = "Reflection_GetFieldValue", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern object? Reflection_GetFieldValue(uint fieldToken, object? target,
            int fieldOffset, int fieldSize, bool isValueType);

        [DllImport("*", EntryPoint = "Reflection_SetFieldValue", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void Reflection_SetFieldValue(uint fieldToken, object? target,
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
    public sealed unsafe class RuntimePropertyInfo : PropertyInfo
    {
        private readonly Type _declaringType;
        private readonly string _name;
        private readonly MethodInfo? _getter;
        private readonly MethodInfo? _setter;
        private Type? _propertyType;

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

        public override Type PropertyType
        {
            get
            {
                if (_propertyType == null && _getter is RuntimeMethodInfo rmi)
                {
                    void* mt = Reflection_GetMethodReturnTypeMethodTable(rmi.AssemblyId, (uint)rmi.MetadataToken);
                    if (mt != null)
                        _propertyType = Type.GetTypeFromHandle(new RuntimeTypeHandle((nint)mt));
                }
                return _propertyType!;
            }
        }

        [DllImport("*", EntryPoint = "Reflection_GetMethodReturnTypeMethodTable", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern void* Reflection_GetMethodReturnTypeMethodTable(uint assemblyId, uint methodToken);

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
                    byte* namePtr = Reflection_GetMethodName(_assemblyId, _methodToken);
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

        public override ParameterInfo[] GetParameters()
        {
            int count = Reflection_GetMethodParameterCount(_assemblyId, _methodToken);
            if (count <= 0)
                return new ParameterInfo[0];

            var parameters = new ParameterInfo[count];
            for (int i = 0; i < count; i++)
            {
                parameters[i] = new RuntimeParameterInfo(_assemblyId, _methodToken, i);
            }
            return parameters;
        }

        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
        {
            return Reflection_InvokeMethod(_assemblyId, _methodToken, obj, parameters);
        }

        public override object Invoke(BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture)
        {
            // Create instance then call constructor
            // For now, simplified - just invoke
            return Reflection_InvokeMethod(_assemblyId, _methodToken, null, parameters) ?? new object();
        }

        public override int MetadataToken => (int)_methodToken;

        /// <summary>
        /// Gets the assembly ID where this constructor is defined.
        /// ProtonOS-specific extension for reflection invoke support.
        /// </summary>
        internal uint AssemblyId => _assemblyId;

        [DllImport("*", EntryPoint = "Reflection_GetMethodParameterCount", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern int Reflection_GetMethodParameterCount(uint assemblyId, uint methodToken);

        [DllImport("*", EntryPoint = "Reflection_GetMethodName", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern byte* Reflection_GetMethodName(uint assemblyId, uint methodToken);

        [DllImport("*", EntryPoint = "Reflection_InvokeMethod", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern object? Reflection_InvokeMethod(uint assemblyId, uint methodToken, object? target, object?[]? args);

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
