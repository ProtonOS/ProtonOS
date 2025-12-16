// ProtonOS korlib - Reflection Base Types
// Minimal abstract base classes for reflection.
// These allow korlib to define concrete RuntimeMethodInfo, etc.

using System.Globalization;

namespace System.Reflection
{
    /// <summary>
    /// Member types enumeration.
    /// </summary>
    [Flags]
    public enum MemberTypes
    {
        Constructor = 1,
        Event = 2,
        Field = 4,
        Method = 8,
        Property = 16,
        TypeInfo = 32,
        Custom = 64,
        NestedType = 128,
        All = Constructor | Event | Field | Method | Property | TypeInfo | NestedType
    }

    /// <summary>
    /// Binding flags for reflection operations.
    /// </summary>
    [Flags]
    public enum BindingFlags
    {
        Default = 0,
        IgnoreCase = 1,
        DeclaredOnly = 2,
        Instance = 4,
        Static = 8,
        Public = 16,
        NonPublic = 32,
        FlattenHierarchy = 64,
        InvokeMethod = 256,
        CreateInstance = 512,
        GetField = 1024,
        SetField = 2048,
        GetProperty = 4096,
        SetProperty = 8192,
        PutDispProperty = 16384,
        PutRefDispProperty = 32768,
        ExactBinding = 65536,
        SuppressChangeType = 131072,
        OptionalParamBinding = 262144,
        IgnoreReturn = 16777216
    }

    /// <summary>
    /// Method attributes.
    /// </summary>
    [Flags]
    public enum MethodAttributes : ushort
    {
        MemberAccessMask = 7,
        PrivateScope = 0,
        Private = 1,
        FamANDAssem = 2,
        Assembly = 3,
        Family = 4,
        FamORAssem = 5,
        Public = 6,
        Static = 16,
        Final = 32,
        Virtual = 64,
        HideBySig = 128,
        VtableLayoutMask = 256,
        ReuseSlot = 0,
        NewSlot = 256,
        CheckAccessOnOverride = 512,
        Abstract = 1024,
        SpecialName = 2048,
        PInvokeImpl = 8192,
        UnmanagedExport = 8,
        RTSpecialName = 4096,
        HasSecurity = 16384,
        RequireSecObject = 32768
    }

    /// <summary>
    /// Field attributes.
    /// </summary>
    [Flags]
    public enum FieldAttributes : ushort
    {
        FieldAccessMask = 7,
        PrivateScope = 0,
        Private = 1,
        FamANDAssem = 2,
        Assembly = 3,
        Family = 4,
        FamORAssem = 5,
        Public = 6,
        Static = 16,
        InitOnly = 32,
        Literal = 64,
        NotSerialized = 128,
        SpecialName = 512,
        PInvokeImpl = 8192,
        RTSpecialName = 1024,
        HasFieldMarshal = 4096,
        HasDefault = 32768,
        HasFieldRVA = 256
    }

    /// <summary>
    /// Property attributes.
    /// </summary>
    [Flags]
    public enum PropertyAttributes : ushort
    {
        None = 0,
        SpecialName = 512,
        RTSpecialName = 1024,
        HasDefault = 4096
    }

    /// <summary>
    /// Base class for all reflection member info.
    /// </summary>
    public abstract class MemberInfo
    {
        public abstract string Name { get; }
        public abstract Type? DeclaringType { get; }
        public abstract MemberTypes MemberType { get; }
        public virtual int MetadataToken => 0;
        public virtual Type? ReflectedType => DeclaringType;

        public virtual object[] GetCustomAttributes(bool inherit) => new object[0];
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) => new object[0];
        public virtual bool IsDefined(Type attributeType, bool inherit) => false;

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();

        // Equality operators - reference comparison
        public static bool operator ==(MemberInfo? left, MemberInfo? right)
        {
            if (left is null)
                return right is null;
            if (right is null)
                return false;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(MemberInfo? left, MemberInfo? right) => !(left == right);
    }

    /// <summary>
    /// Base class for method-like members.
    /// </summary>
    public abstract class MethodBase : MemberInfo
    {
        /// <summary>
        /// Gets a MethodBase from a RuntimeMethodHandle.
        /// The handle contains (assemblyId &lt;&lt; 32) | methodToken.
        /// </summary>
        public static MethodBase? GetMethodFromHandle(RuntimeMethodHandle handle)
        {
#if KORLIB_IL
            // Stub for IL build - should be resolved to AOT implementation
            return null;
#else
            if (handle.Value == IntPtr.Zero)
                return null;
            // Decode: high 32 bits = assemblyId, low 32 bits = token
            ulong value = (ulong)handle.Value;
            uint assemblyId = (uint)(value >> 32);
            uint token = (uint)(value & 0xFFFFFFFF);
            if (assemblyId == 0 || token == 0)
                return null;
            // Create RuntimeMethodInfo - declaringType will be resolved later if needed
            return new RuntimeMethodInfo(assemblyId, token, null!);
#endif
        }

        public abstract RuntimeMethodHandle MethodHandle { get; }
        public abstract MethodAttributes Attributes { get; }
        public abstract ParameterInfo[] GetParameters();

        public virtual CallingConventions CallingConvention => CallingConventions.Standard;
        public virtual bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;
        public virtual bool IsConstructor => (Attributes & MethodAttributes.RTSpecialName) != 0 && Name == ".ctor";
        public virtual bool IsFinal => (Attributes & MethodAttributes.Final) != 0;
        public virtual bool IsPrivate => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
        public virtual bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        public virtual bool IsStatic => (Attributes & MethodAttributes.Static) != 0;
        public virtual bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;

        public abstract object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture);

        public object? Invoke(object? obj, object?[]? parameters)
        {
            return Invoke(obj, BindingFlags.Default, null, parameters, null);
        }

        public static bool operator ==(MethodBase? left, MethodBase? right)
        {
            if ((object?)left == null)
                return (object?)right == null;
            if ((object?)right == null)
                return false;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(MethodBase? left, MethodBase? right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    /// <summary>
    /// Represents a method.
    /// </summary>
    public abstract class MethodInfo : MethodBase
    {
        public override MemberTypes MemberType => MemberTypes.Method;
        public virtual Type ReturnType => null!;
        public abstract MethodInfo GetBaseDefinition();
        public abstract ICustomAttributeProvider ReturnTypeCustomAttributes { get; }

        public static bool operator ==(MethodInfo? left, MethodInfo? right)
        {
            if ((object?)left == null)
                return (object?)right == null;
            if ((object?)right == null)
                return false;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(MethodInfo? left, MethodInfo? right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    /// <summary>
    /// Represents a constructor.
    /// </summary>
    public abstract class ConstructorInfo : MethodBase
    {
        public const string ConstructorName = ".ctor";
        public const string TypeConstructorName = ".cctor";

        public override MemberTypes MemberType => MemberTypes.Constructor;

        public abstract object Invoke(BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, CultureInfo? culture);

        public static bool operator ==(ConstructorInfo? left, ConstructorInfo? right)
        {
            if ((object?)left == null)
                return (object?)right == null;
            if ((object?)right == null)
                return false;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(ConstructorInfo? left, ConstructorInfo? right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    /// <summary>
    /// Represents a field.
    /// </summary>
    public abstract class FieldInfo : MemberInfo
    {
        /// <summary>
        /// Gets a FieldInfo from a RuntimeFieldHandle.
        /// The handle contains (assemblyId &lt;&lt; 32) | fieldToken.
        /// </summary>
        public static FieldInfo? GetFieldFromHandle(RuntimeFieldHandle handle)
        {
#if KORLIB_IL
            // Stub for IL build - should be resolved to AOT implementation
            return null;
#else
            if (handle.Value == IntPtr.Zero)
                return null;
            // Decode: high 32 bits = assemblyId, low 32 bits = token
            ulong value = (ulong)handle.Value;
            uint assemblyId = (uint)(value >> 32);
            uint token = (uint)(value & 0xFFFFFFFF);
            if (assemblyId == 0 || token == 0)
                return null;
            // Create RuntimeFieldInfo - field details will be resolved via reflection exports
            return new RuntimeFieldInfo(assemblyId, token, null!, 0, 0, false);
#endif
        }

        public override MemberTypes MemberType => MemberTypes.Field;
        public abstract RuntimeFieldHandle FieldHandle { get; }
        public abstract Type FieldType { get; }
        public abstract FieldAttributes Attributes { get; }

        public bool IsInitOnly => (Attributes & FieldAttributes.InitOnly) != 0;
        public bool IsLiteral => (Attributes & FieldAttributes.Literal) != 0;
        public bool IsPrivate => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private;
        public bool IsPublic => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
        public bool IsStatic => (Attributes & FieldAttributes.Static) != 0;

        public abstract object? GetValue(object? obj);

        public abstract void SetValue(object? obj, object? value, BindingFlags invokeAttr,
            Binder? binder, CultureInfo? culture);

        public void SetValue(object? obj, object? value)
        {
            SetValue(obj, value, BindingFlags.Default, null, null);
        }

        public static bool operator ==(FieldInfo? left, FieldInfo? right)
        {
            if ((object?)left == null)
                return (object?)right == null;
            if ((object?)right == null)
                return false;
            return ReferenceEquals(left, right);
        }

        public static bool operator !=(FieldInfo? left, FieldInfo? right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => base.GetHashCode();
    }

    /// <summary>
    /// Represents a property.
    /// </summary>
    public abstract class PropertyInfo : MemberInfo
    {
        public override MemberTypes MemberType => MemberTypes.Property;
        public abstract Type PropertyType { get; }
        public abstract PropertyAttributes Attributes { get; }
        public abstract bool CanRead { get; }
        public abstract bool CanWrite { get; }

        public abstract MethodInfo? GetGetMethod(bool nonPublic);
        public abstract MethodInfo? GetSetMethod(bool nonPublic);
        public abstract ParameterInfo[] GetIndexParameters();
        public abstract MethodInfo[] GetAccessors(bool nonPublic);

        public MethodInfo? GetGetMethod() => GetGetMethod(false);
        public MethodInfo? GetSetMethod() => GetSetMethod(false);
        public MethodInfo[] GetAccessors() => GetAccessors(false);

        public abstract object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? index, CultureInfo? culture);

        public abstract void SetValue(object? obj, object? value, BindingFlags invokeAttr,
            Binder? binder, object?[]? index, CultureInfo? culture);

        public object? GetValue(object? obj) => GetValue(obj, null);
        public object? GetValue(object? obj, object?[]? index) =>
            GetValue(obj, BindingFlags.Default, null, index, null);

        public void SetValue(object? obj, object? value) => SetValue(obj, value, null);
        public void SetValue(object? obj, object? value, object?[]? index) =>
            SetValue(obj, value, BindingFlags.Default, null, index, null);
    }

    /// <summary>
    /// Provides information about a method parameter.
    /// </summary>
    public class ParameterInfo
    {
        public virtual string? Name => null;
        public virtual Type ParameterType => null!;
        public virtual int Position => 0;
        public virtual ParameterAttributes Attributes => ParameterAttributes.None;
        public virtual object? DefaultValue => null;
        public virtual bool HasDefaultValue => false;
        public bool IsIn => (Attributes & ParameterAttributes.In) != 0;
        public bool IsOut => (Attributes & ParameterAttributes.Out) != 0;
        public bool IsOptional => (Attributes & ParameterAttributes.Optional) != 0;

        public virtual object[] GetCustomAttributes(bool inherit) => new object[0];
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) => new object[0];
        public virtual bool IsDefined(Type attributeType, bool inherit) => false;
    }

    /// <summary>
    /// Parameter attributes.
    /// </summary>
    [Flags]
    public enum ParameterAttributes
    {
        None = 0,
        In = 1,
        Out = 2,
        Lcid = 4,
        Retval = 8,
        Optional = 16,
        HasDefault = 4096,
        HasFieldMarshal = 8192
    }

    /// <summary>
    /// Calling conventions.
    /// </summary>
    [Flags]
    public enum CallingConventions
    {
        Standard = 1,
        VarArgs = 2,
        Any = Standard | VarArgs,
        HasThis = 32,
        ExplicitThis = 64
    }

    /// <summary>
    /// Custom attribute provider interface.
    /// </summary>
    public interface ICustomAttributeProvider
    {
        object[] GetCustomAttributes(bool inherit);
        object[] GetCustomAttributes(Type attributeType, bool inherit);
        bool IsDefined(Type attributeType, bool inherit);
    }

    /// <summary>
    /// Binder for binding operations.
    /// </summary>
    public abstract class Binder
    {
        public abstract MethodBase BindToMethod(BindingFlags bindingAttr, MethodBase[] match, ref object?[] args,
            ParameterModifier[]? modifiers, CultureInfo? culture, string[]? names, out object? state);
        public abstract FieldInfo BindToField(BindingFlags bindingAttr, FieldInfo[] match, object value, CultureInfo? culture);
        public abstract object ChangeType(object value, Type type, CultureInfo? culture);
        public abstract void ReorderArgumentArray(ref object?[] args, object state);
        public abstract MethodBase? SelectMethod(BindingFlags bindingAttr, MethodBase[] match, Type[] types, ParameterModifier[]? modifiers);
        public abstract PropertyInfo? SelectProperty(BindingFlags bindingAttr, PropertyInfo[] match, Type? returnType, Type[]? indexes, ParameterModifier[]? modifiers);
    }

    /// <summary>
    /// Parameter modifier struct.
    /// </summary>
    public readonly struct ParameterModifier
    {
        private readonly bool[] _byRef;

        public ParameterModifier(int parameterCount)
        {
            _byRef = new bool[parameterCount];
        }

        public bool this[int index]
        {
            get => _byRef[index];
            set => _byRef[index] = value;
        }
    }

    /// <summary>
    /// Base class for assembly representation in reflection.
    /// Minimal korlib implementation - full features in SystemRuntime.
    /// </summary>
    public abstract class Assembly : ICustomAttributeProvider
    {
        /// <summary>Gets the display name of the assembly.</summary>
        public virtual string FullName => string.Empty;

        /// <summary>Gets the types defined in this assembly.</summary>
        public virtual Type[] GetTypes() => Array.Empty<Type>();

        /// <summary>Gets the public types defined in this assembly.</summary>
        public virtual Type[] GetExportedTypes() => GetTypes();

        /// <summary>Gets the Type object with the specified name in the assembly.</summary>
        public virtual Type? GetType(string name) => GetType(name, false, false);

        /// <summary>Gets the Type object with the specified name, optionally throwing.</summary>
        public virtual Type? GetType(string name, bool throwOnError) => GetType(name, throwOnError, false);

        /// <summary>Gets the Type object with the specified name.</summary>
        public virtual Type? GetType(string name, bool throwOnError, bool ignoreCase) => null;

        /// <summary>Gets custom attributes applied to this assembly.</summary>
        public virtual object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();

        /// <summary>Gets custom attributes of the specified type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<object>();

        /// <summary>Determines whether the specified attribute is defined.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit) => false;

        /// <summary>Returns a string representation.</summary>
        public override string ToString() => FullName;
    }

#if !KORLIB_IL
    /// <summary>
    /// Helper class to force bflat to keep virtual method vtable entries.
    /// This prevents dead code elimination from removing vtable slots that JIT code needs.
    /// </summary>
    public static class ReflectionVtableKeeper
    {
        // Static fields to defeat dead code elimination
        private static RuntimeMethodInfo? _keepRmi;
        private static RuntimeFieldInfo? _keepRfi;
        private static RuntimeConstructorInfo? _keepRci;

        /// <summary>
        /// Force bflat to keep virtual method vtable entries by explicitly calling them.
        /// Called from kernel init.
        /// NOTE: Only force-keep the specific methods needed for JIT reflection.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
            System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public static void ForceKeepVtableMethods()
        {
            // Use static fields to prevent compiler from proving null
            if (_keepRmi != null)
            {
                // Force MemberInfo virtual methods needed by reflection iteration
                _ = _keepRmi.Name;
            }

            if (_keepRfi != null)
            {
                _ = _keepRfi.Name;
            }

            if (_keepRci != null)
            {
                _ = _keepRci.Name;
            }
        }
    }
#endif
}
