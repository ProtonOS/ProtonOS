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

        /// <summary>Returns a list of CustomAttributeData objects representing data about the attributes.</summary>
        public virtual System.Collections.Generic.IList<CustomAttributeData> GetCustomAttributesData()
            => new System.Collections.Generic.List<CustomAttributeData>();

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

        // Visibility/access properties
        public virtual bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;
        public virtual bool IsAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly;
        public virtual bool IsConstructor => (Attributes & MethodAttributes.RTSpecialName) != 0 && Name == ".ctor";
        public virtual bool IsFamily => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family;
        public virtual bool IsFamilyAndAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamANDAssem;
        public virtual bool IsFamilyOrAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem;
        public virtual bool IsFinal => (Attributes & MethodAttributes.Final) != 0;
        public virtual bool IsHideBySig => (Attributes & MethodAttributes.HideBySig) != 0;
        public virtual bool IsPrivate => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
        public virtual bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        public virtual bool IsSpecialName => (Attributes & MethodAttributes.SpecialName) != 0;
        public virtual bool IsStatic => (Attributes & MethodAttributes.Static) != 0;
        public virtual bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;

        // Generic method properties
        public virtual bool ContainsGenericParameters => false;
        public virtual bool IsGenericMethod => false;
        public virtual bool IsGenericMethodDefinition => false;

        /// <summary>Returns an array of Type objects that represent the type arguments of a generic method.</summary>
        public virtual Type[] GetGenericArguments() => Array.Empty<Type>();

        // Security properties (always return safe defaults for ProtonOS)
        public virtual bool IsSecurityCritical => true;
        public virtual bool IsSecuritySafeCritical => false;
        public virtual bool IsSecurityTransparent => false;

        // Method implementation
        public virtual MethodImplAttributes MethodImplementationFlags => MethodImplAttributes.IL;

        /// <summary>Returns the MethodBody object that provides access to the MSIL stream, local variables, and exceptions.</summary>
        public virtual MethodBody? GetMethodBody() => null;

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

        /// <summary>Gets the return parameter of the method.</summary>
        public virtual ParameterInfo? ReturnParameter => null;

        /// <summary>Gets the return type of this method.</summary>
        public virtual Type ReturnType => typeof(void);

        /// <summary>Gets the custom attributes for the return type.</summary>
        public abstract ICustomAttributeProvider ReturnTypeCustomAttributes { get; }

        /// <summary>Returns the MethodInfo object for the method on the direct or indirect base class in which the method was first declared.</summary>
        public abstract MethodInfo GetBaseDefinition();

        /// <summary>Returns a MethodInfo object that represents a generic method definition from which the current method can be constructed.</summary>
        public virtual MethodInfo GetGenericMethodDefinition()
        {
            throw new InvalidOperationException("This method is not generic.");
        }

        /// <summary>Substitutes the elements of an array of types for the type parameters of the current generic method definition.</summary>
        public virtual MethodInfo MakeGenericMethod(params Type[] typeArguments)
        {
            throw new InvalidOperationException("This method is not a generic method definition.");
        }

        /// <summary>Creates a delegate of the specified type from this method.</summary>
        public virtual Delegate CreateDelegate(Type delegateType)
        {
            throw new NotSupportedException();
        }

        /// <summary>Creates a delegate of the specified type with the specified target from this method.</summary>
        public virtual Delegate CreateDelegate(Type delegateType, object? target)
        {
            throw new NotSupportedException();
        }

        /// <summary>Creates a delegate of type T from this method.</summary>
        public T CreateDelegate<T>() where T : Delegate
        {
            return (T)CreateDelegate(typeof(T));
        }

        /// <summary>Creates a delegate of type T with the specified target from this method.</summary>
        public T CreateDelegate<T>(object? target) where T : Delegate
        {
            return (T)CreateDelegate(typeof(T), target);
        }

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
        /// <summary>Represents the name of the class constructor method as it is stored in metadata.</summary>
        public const string ConstructorName = ".ctor";

        /// <summary>Represents the name of the type constructor method as it is stored in metadata.</summary>
        public const string TypeConstructorName = ".cctor";

        public override MemberTypes MemberType => MemberTypes.Constructor;

        /// <summary>Invokes the constructor with the specified parameters.</summary>
        public object Invoke(object?[]? parameters)
        {
            return Invoke(BindingFlags.Default, null, parameters, null)!;
        }

        /// <summary>When implemented in a derived class, invokes the constructor with the specified arguments.</summary>
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
    /// Performs reflection on a module.
    /// </summary>
    public abstract class Module : ICustomAttributeProvider
    {
        /// <summary>Gets the appropriate Assembly for this instance of Module.</summary>
        public virtual Assembly Assembly => throw new NotImplementedException();

        /// <summary>Gets a String representing the fully qualified name and path to this module.</summary>
        public virtual string FullyQualifiedName => string.Empty;

        /// <summary>Gets a string representing the name of the module with the path removed.</summary>
        public virtual string Name => string.Empty;

        /// <summary>Gets a token that identifies the module in metadata.</summary>
        public virtual int MetadataToken => 0;

        /// <summary>Gets the GUID for this module.</summary>
        public virtual Guid ModuleVersionId => Guid.Empty;

        /// <summary>Gets a pair of values indicating the nature of the code in a module and the platform targeted by the module.</summary>
        public virtual void GetPEKind(out PortableExecutableKinds peKind, out ImageFileMachine machine)
        {
            peKind = PortableExecutableKinds.ILOnly;
            machine = ImageFileMachine.I386;
        }

        /// <summary>Gets a value indicating whether the object is a resource.</summary>
        public virtual bool IsResource() => false;

        /// <summary>Returns the specified type.</summary>
        public virtual Type? GetType(string className) => GetType(className, false, false);

        /// <summary>Returns the specified type, searching the module with the specified case sensitivity.</summary>
        public virtual Type? GetType(string className, bool ignoreCase) => GetType(className, false, ignoreCase);

        /// <summary>Returns the specified type, specifying whether to throw an exception if the type is not found.</summary>
        public virtual Type? GetType(string className, bool throwOnError, bool ignoreCase)
        {
            if (className == null) throw new ArgumentNullException(nameof(className));
            return null;
        }

        /// <summary>Returns all the types defined within this module.</summary>
        public virtual Type[] GetTypes() => Array.Empty<Type>();

        /// <summary>Returns an array of classes accepted by the given filter and filter criteria.</summary>
        public virtual Type[] FindTypes(TypeFilter? filter, object? filterCriteria)
        {
            var types = GetTypes();
            if (filter == null) return types;

            var result = new System.Collections.Generic.List<Type>();
            foreach (var type in types)
            {
                if (filter(type, filterCriteria))
                    result.Add(type);
            }
            return result.ToArray();
        }

        /// <summary>Gets a collection of custom attributes.</summary>
        public virtual object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();

        /// <summary>Gets a collection of custom attributes of the specified type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Array.Empty<object>();
        }

        /// <summary>Determines whether custom attributes of the specified type are applied to this module.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return false;
        }

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Represents a filter to find types in a module.
    /// </summary>
    public delegate bool TypeFilter(Type m, object? filterCriteria);

    /// <summary>
    /// Specifies the nature of the code in an executable file.
    /// </summary>
    [Flags]
    public enum PortableExecutableKinds
    {
        /// <summary>The executable is not in the portable executable (PE) file format.</summary>
        NotAPortableExecutableImage = 0,
        /// <summary>The executable contains only MSIL, and is therefore neutral with respect to 32-bit or 64-bit platforms.</summary>
        ILOnly = 1,
        /// <summary>The executable can be run only on a 32-bit platform, or in the 32-bit Windows on Windows (WoW) environment on a 64-bit platform.</summary>
        Required32Bit = 2,
        /// <summary>The executable requires a 64-bit platform.</summary>
        PE32Plus = 4,
        /// <summary>The executable contains pure unmanaged code.</summary>
        Unmanaged32Bit = 8,
        /// <summary>The executable is platform-agnostic but should be run on a 32-bit platform whenever possible.</summary>
        Preferred32Bit = 16,
    }

    /// <summary>
    /// Identifies the platform targeted by an executable.
    /// </summary>
    public enum ImageFileMachine
    {
        /// <summary>Targets a 32-bit Intel processor.</summary>
        I386 = 332,
        /// <summary>Targets a 64-bit AMD processor.</summary>
        AMD64 = 34404,
        /// <summary>Targets a 64-bit Intel Itanium processor.</summary>
        IA64 = 512,
        /// <summary>Targets an ARM processor.</summary>
        ARM = 452,
    }

    /// <summary>
    /// Base class for assembly representation in reflection.
    /// </summary>
    public abstract class Assembly : ICustomAttributeProvider
    {
        /// <summary>Gets the display name of the assembly.</summary>
        public virtual string FullName => string.Empty;

        /// <summary>Gets the location of the assembly as specified originally.</summary>
        public virtual string Location => string.Empty;

        /// <summary>Gets the code base of the assembly as a URL.</summary>
        [Obsolete("Assembly.CodeBase and Assembly.EscapedCodeBase are obsolete.")]
        public virtual string? CodeBase => null;

        /// <summary>Gets a value indicating whether the assembly was loaded from the global assembly cache.</summary>
        [Obsolete("The Global Assembly Cache is not supported.")]
        public virtual bool GlobalAssemblyCache => false;

        /// <summary>Gets the entry point of this assembly.</summary>
        public virtual MethodInfo? EntryPoint => null;

        /// <summary>Gets a Boolean value indicating whether the assembly is loaded for reflection only.</summary>
        [Obsolete("ReflectionOnly loading is not supported.")]
        public virtual bool ReflectionOnly => false;

        /// <summary>Gets a value that indicates which set of security rules the common language runtime enforces for this assembly.</summary>
        public virtual bool IsFullyTrusted => true;

        /// <summary>Gets a value that indicates whether the current assembly was generated dynamically in the current process.</summary>
        public virtual bool IsDynamic => false;

        /// <summary>Gets the host context with which the assembly was loaded.</summary>
        public virtual long HostContext => 0;

        /// <summary>Gets the module that contains the manifest for the current assembly.</summary>
        public virtual Module ManifestModule => throw new NotImplementedException();

        /// <summary>Gets an AssemblyName for this assembly.</summary>
        public virtual AssemblyName GetName() => GetName(false);

        /// <summary>Gets an AssemblyName for this assembly, optionally copying the public key token.</summary>
        public virtual AssemblyName GetName(bool copiedName) => new AssemblyName(FullName);

        /// <summary>Gets the types defined in this assembly.</summary>
        public virtual Type[] GetTypes() => GetExportedTypes();

        /// <summary>Gets the public types defined in this assembly.</summary>
        public virtual Type[] GetExportedTypes() => Array.Empty<Type>();

        /// <summary>Gets the Type object with the specified name in the assembly.</summary>
        public virtual Type? GetType(string name) => GetType(name, false, false);

        /// <summary>Gets the Type object with the specified name, optionally throwing.</summary>
        public virtual Type? GetType(string name, bool throwOnError) => GetType(name, throwOnError, false);

        /// <summary>Gets the Type object with the specified name.</summary>
        public virtual Type? GetType(string name, bool throwOnError, bool ignoreCase)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return null;
        }

        /// <summary>Gets the specified module in this assembly.</summary>
        public virtual Module? GetModule(string name) => null;

        /// <summary>Gets all the modules that are part of this assembly.</summary>
        public virtual Module[] GetModules() => GetModules(false);

        /// <summary>Gets all the modules that are part of this assembly, specifying whether to include resource modules.</summary>
        public virtual Module[] GetModules(bool getResourceModules) => Array.Empty<Module>();

        /// <summary>Gets all the loaded modules that are part of this assembly.</summary>
        public virtual Module[] GetLoadedModules() => GetLoadedModules(false);

        /// <summary>Gets all the loaded modules that are part of this assembly, specifying whether to include resource modules.</summary>
        public virtual Module[] GetLoadedModules(bool getResourceModules) => GetModules(getResourceModules);

        /// <summary>Gets the AssemblyName objects for all the assemblies referenced by this assembly.</summary>
        public virtual AssemblyName[] GetReferencedAssemblies() => Array.Empty<AssemblyName>();

        /// <summary>Gets custom attributes applied to this assembly.</summary>
        public virtual object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();

        /// <summary>Gets custom attributes of the specified type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Array.Empty<object>();
        }

        /// <summary>Determines whether the specified attribute is defined.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return false;
        }

        /// <summary>Gets the satellite assembly for the specified culture.</summary>
        public virtual Assembly? GetSatelliteAssembly(Globalization.CultureInfo culture)
            => throw new NotSupportedException();

        /// <summary>Gets the satellite assembly for the specified culture and version.</summary>
        public virtual Assembly? GetSatelliteAssembly(Globalization.CultureInfo culture, Version? version)
            => throw new NotSupportedException();

        /// <summary>Locates the specified type from this assembly and creates an instance of it using the system activator.</summary>
        public object? CreateInstance(string typeName) => CreateInstance(typeName, false);

        /// <summary>Locates the specified type from this assembly and creates an instance of it using the system activator, with optional case-insensitive search.</summary>
        public object? CreateInstance(string typeName, bool ignoreCase)
        {
            var type = GetType(typeName, false, ignoreCase);
            if (type == null) return null;
            return Activator.CreateInstance(type);
        }

        /// <summary>Returns the names of all the resources in this assembly.</summary>
        public virtual string[] GetManifestResourceNames() => Array.Empty<string>();

        /// <summary>Loads the specified manifest resource from this assembly.</summary>
        public virtual System.IO.Stream? GetManifestResourceStream(string name) => null;

        /// <summary>Loads the specified manifest resource, scoped by the namespace of the specified type, from this assembly.</summary>
        public virtual System.IO.Stream? GetManifestResourceStream(Type type, string name)
            => GetManifestResourceStream(type.Namespace + "." + name);

        /// <summary>Returns information about how the given resource has been persisted.</summary>
        public virtual ManifestResourceInfo? GetManifestResourceInfo(string resourceName) => null;

        /// <summary>Gets the currently loaded assembly in which the specified type is defined.</summary>
        public static Assembly? GetAssembly(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            return null;
        }

        /// <summary>Gets the assembly that contains the code that is currently executing.</summary>
        public static Assembly GetExecutingAssembly() => throw new NotImplementedException();

        /// <summary>Returns the Assembly of the method that invoked the currently executing method.</summary>
        public static Assembly GetCallingAssembly() => throw new NotImplementedException();

        /// <summary>Gets the process executable in the default application domain.</summary>
        public static Assembly? GetEntryAssembly() => null;

        /// <summary>Loads an assembly given its AssemblyName.</summary>
        public static Assembly Load(AssemblyName assemblyRef) => throw new NotImplementedException();

        /// <summary>Loads an assembly given its display name.</summary>
        public static Assembly Load(string assemblyString) => throw new NotImplementedException();

        /// <summary>Loads the assembly with a common object file format (COFF) based image containing an emitted assembly.</summary>
        public static Assembly Load(byte[] rawAssembly) => throw new NotImplementedException();

        /// <summary>Loads the assembly with a common object file format (COFF) based image containing an emitted assembly, optionally including symbols for the assembly.</summary>
        public static Assembly Load(byte[] rawAssembly, byte[]? rawSymbolStore) => throw new NotImplementedException();

        /// <summary>Loads an assembly given its file name or path.</summary>
        public static Assembly LoadFrom(string assemblyFile) => throw new NotImplementedException();

        /// <summary>Loads an assembly given its path.</summary>
        public static Assembly LoadFile(string path) => throw new NotImplementedException();

        /// <summary>Returns a string representation.</summary>
        public override string ToString() => FullName;

        /// <summary>Determines whether this assembly and the specified object are equal.</summary>
        public override bool Equals(object? o) => o is Assembly asm && asm.FullName == FullName;

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode() => FullName.GetHashCode();

        /// <summary>Indicates whether two Assembly objects are equal.</summary>
        public static bool operator ==(Assembly? left, Assembly? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two Assembly objects are not equal.</summary>
        public static bool operator !=(Assembly? left, Assembly? right) => !(left == right);
    }

    /// <summary>
    /// Describes an assembly's unique identity in full.
    /// </summary>
    public sealed class AssemblyName : ICloneable
    {
        private string? _name;
        private Version? _version;
        private Globalization.CultureInfo? _cultureInfo;
        private byte[]? _publicKeyToken;
        private string? _codeBase;
        private AssemblyNameFlags _flags;

        /// <summary>Initializes a new instance of the AssemblyName class.</summary>
        public AssemblyName() { }

        /// <summary>Initializes a new instance of the AssemblyName class with the specified display name.</summary>
        public AssemblyName(string assemblyName)
        {
            if (assemblyName == null) throw new ArgumentNullException(nameof(assemblyName));
            _name = assemblyName;
        }

        /// <summary>Gets or sets the simple name of the assembly.</summary>
        public string? Name
        {
            get => _name;
            set => _name = value;
        }

        /// <summary>Gets or sets the major, minor, build, and revision numbers of the assembly.</summary>
        public Version? Version
        {
            get => _version;
            set => _version = value;
        }

        /// <summary>Gets or sets the culture supported by the assembly.</summary>
        public Globalization.CultureInfo? CultureInfo
        {
            get => _cultureInfo;
            set => _cultureInfo = value;
        }

        /// <summary>Gets or sets the name of the culture associated with the assembly.</summary>
        public string? CultureName
        {
            get => _cultureInfo?.Name;
            set => _cultureInfo = value == null ? null : new Globalization.CultureInfo(value);
        }

        /// <summary>Gets or sets the location of the assembly as a URL.</summary>
        public string? CodeBase
        {
            get => _codeBase;
            set => _codeBase = value;
        }

        /// <summary>Gets or sets the attributes of the assembly.</summary>
        public AssemblyNameFlags Flags
        {
            get => _flags;
            set => _flags = value;
        }

        /// <summary>Gets the full name of the assembly.</summary>
        public string FullName
        {
            get
            {
                if (_name == null) return string.Empty;
                var sb = new System.Text.StringBuilder(_name);
                if (_version is not null)
                {
                    sb.Append(", Version=");
                    sb.Append(_version.ToString());
                }
                return sb.ToString();
            }
        }

        /// <summary>Gets the public key token, which is the last 8 bytes of the SHA-1 hash of the public key under which the assembly is signed.</summary>
        public byte[]? GetPublicKeyToken() => _publicKeyToken;

        /// <summary>Sets the public key token, which is the last 8 bytes of the SHA-1 hash of the public key under which the assembly is signed.</summary>
        public void SetPublicKeyToken(byte[]? publicKeyToken) => _publicKeyToken = publicKeyToken;

        /// <summary>Gets the public key of the assembly.</summary>
        public byte[]? GetPublicKey() => null;

        /// <summary>Sets the public key identifying the assembly.</summary>
        public void SetPublicKey(byte[]? publicKey) { }

        /// <summary>Makes a copy of this AssemblyName object.</summary>
        public object Clone()
        {
            var clone = new AssemblyName();
            clone._name = _name;
            clone._version = _version;
            clone._cultureInfo = _cultureInfo;
            clone._publicKeyToken = _publicKeyToken;
            clone._codeBase = _codeBase;
            clone._flags = _flags;
            return clone;
        }

        /// <summary>Returns the full name of the assembly.</summary>
        public override string ToString() => FullName;

        /// <summary>Gets the AssemblyName for a given file.</summary>
        public static AssemblyName GetAssemblyName(string assemblyFile) => throw new NotImplementedException();
    }

    /// <summary>
    /// Provides information about an assembly's manifest resource.
    /// </summary>
    public class ManifestResourceInfo
    {
        /// <summary>Gets the containing assembly for the manifest resource.</summary>
        public virtual Assembly? ReferencedAssembly => null;

        /// <summary>Gets the name of the file that contains the manifest resource.</summary>
        public virtual string? FileName => null;

        /// <summary>Gets the manifest resource's location.</summary>
        public virtual ResourceLocation ResourceLocation => ResourceLocation.ContainedInManifestFile;
    }

    /// <summary>
    /// Specifies the locations of a manifest resource.
    /// </summary>
    [Flags]
    public enum ResourceLocation
    {
        /// <summary>Specifies that the resource is contained in a manifest file.</summary>
        ContainedInManifestFile = 2,
        /// <summary>Specifies that the resource is contained in another assembly.</summary>
        ContainedInAnotherAssembly = 1,
        /// <summary>Specifies an embedded (that is, non-linked) resource.</summary>
        Embedded = 4,
    }

    /// <summary>
    /// Provides information about the attributes that have been applied to an assembly.
    /// </summary>
    [Flags]
    public enum AssemblyNameFlags
    {
        /// <summary>Specifies that no flags are in effect.</summary>
        None = 0,
        /// <summary>Specifies that a public key is formed from the full public key rather than the public key token.</summary>
        PublicKey = 1,
        /// <summary>Specifies that just-in-time (JIT) compiler optimization is disabled for the assembly.</summary>
        EnableJITcompileOptimizer = 16384,
        /// <summary>Specifies that just-in-time (JIT) compiler tracking is enabled for the assembly.</summary>
        EnableJITcompileTracking = 32768,
        /// <summary>Specifies that the assembly can be retargeted at runtime to an assembly from a different publisher.</summary>
        Retargetable = 256,
    }

    /// <summary>
    /// Specifies flags for the attributes of a method implementation.
    /// </summary>
    [Flags]
    public enum MethodImplAttributes
    {
        /// <summary>Specifies that the method implementation is in IL.</summary>
        IL = 0,
        /// <summary>Specifies that the method implementation is native.</summary>
        Native = 1,
        /// <summary>Specifies that the method implementation is in optimized IL.</summary>
        OPTIL = 2,
        /// <summary>Specifies that the method is provided by the runtime.</summary>
        Runtime = 3,
        /// <summary>Specifies flags about code type.</summary>
        CodeTypeMask = 3,
        /// <summary>Specifies whether the code is managed.</summary>
        ManagedMask = 4,
        /// <summary>Specifies that the method is implemented in unmanaged code.</summary>
        Unmanaged = 4,
        /// <summary>Specifies that the method is implemented in managed code.</summary>
        Managed = 0,
        /// <summary>Specifies that the method is not defined.</summary>
        ForwardRef = 16,
        /// <summary>Specifies that the method signature is exported exactly as declared.</summary>
        PreserveSig = 128,
        /// <summary>Specifies an internal call.</summary>
        InternalCall = 4096,
        /// <summary>Specifies that the method is single-threaded through the body.</summary>
        Synchronized = 32,
        /// <summary>Specifies that the method cannot be inlined.</summary>
        NoInlining = 8,
        /// <summary>Specifies that the method is never optimized.</summary>
        NoOptimization = 64,
        /// <summary>Specifies that the method should be inlined if possible.</summary>
        AggressiveInlining = 256,
        /// <summary>Specifies that the method should be aggressively optimized.</summary>
        AggressiveOptimization = 512,
        /// <summary>Specifies a range check value.</summary>
        MaxMethodImplVal = 65535,
    }

    /// <summary>
    /// Provides information about the body of a method.
    /// </summary>
    public class MethodBody
    {
        /// <summary>Gets a metadata token for the signature that describes the local variables.</summary>
        public virtual int LocalSignatureMetadataToken => 0;

        /// <summary>Gets the list of local variables declared in the method body.</summary>
        public virtual LocalVariableInfo[] LocalVariables => new LocalVariableInfo[0];

        /// <summary>Gets the maximum number of items on the operand stack when the method is executing.</summary>
        public virtual int MaxStackSize => 0;

        /// <summary>Gets a value indicating whether local variables are initialized to default values.</summary>
        public virtual bool InitLocals => true;

        /// <summary>Gets a list that includes all the exception-handling clauses in the method body.</summary>
        public virtual ExceptionHandlingClause[] ExceptionHandlingClauses => new ExceptionHandlingClause[0];

        /// <summary>Returns the MSIL for the method body, as an array of bytes.</summary>
        public virtual byte[]? GetILAsByteArray() => null;
    }

    /// <summary>
    /// Provides information about a local variable in a method body.
    /// </summary>
    public class LocalVariableInfo
    {
        /// <summary>Gets the index of the local variable within the method body.</summary>
        public virtual int LocalIndex => 0;

        /// <summary>Gets the type of the local variable.</summary>
        public virtual Type LocalType => typeof(object);

        /// <summary>Gets a value that indicates whether the local variable is pinned in memory.</summary>
        public virtual bool IsPinned => false;

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString() => $"{LocalType.Name} ({LocalIndex})";
    }

    /// <summary>
    /// Represents a clause in a structured exception-handling block.
    /// </summary>
    public class ExceptionHandlingClause
    {
        /// <summary>Gets the type of exception handled by this clause.</summary>
        public virtual Type? CatchType => null;

        /// <summary>Gets a value indicating the type of this clause.</summary>
        public virtual ExceptionHandlingClauseOptions Flags => ExceptionHandlingClauseOptions.Clause;

        /// <summary>Gets the offset within the method body of the user-supplied filter code.</summary>
        public virtual int FilterOffset => 0;

        /// <summary>Gets the length, in bytes, of the body of this exception-handling clause.</summary>
        public virtual int HandlerLength => 0;

        /// <summary>Gets the offset within the method body of this exception-handling clause.</summary>
        public virtual int HandlerOffset => 0;

        /// <summary>Gets the length, in bytes, of the try block.</summary>
        public virtual int TryLength => 0;

        /// <summary>Gets the offset within the method of the try block.</summary>
        public virtual int TryOffset => 0;
    }

    /// <summary>
    /// Identifies kinds of exception-handling clauses.
    /// </summary>
    [Flags]
    public enum ExceptionHandlingClauseOptions
    {
        /// <summary>The clause accepts all exceptions that derive from a specified type.</summary>
        Clause = 0,
        /// <summary>The clause is executed if an exception occurs, but not on normal completion.</summary>
        Filter = 1,
        /// <summary>The clause is executed whenever the try block exits.</summary>
        Finally = 2,
        /// <summary>The clause is executed if an exception occurs, but not on normal control flow.</summary>
        Fault = 4,
    }

    /// <summary>
    /// Provides information about custom attribute data.
    /// </summary>
    public class CustomAttributeData
    {
        private readonly Type _attributeType;
        private readonly ConstructorInfo? _constructor;
        private readonly System.Collections.Generic.IList<CustomAttributeTypedArgument> _constructorArguments;
        private readonly System.Collections.Generic.IList<CustomAttributeNamedArgument> _namedArguments;

        /// <summary>Initializes a new instance of CustomAttributeData.</summary>
        protected CustomAttributeData()
        {
            _attributeType = typeof(Attribute);
            _constructorArguments = new System.Collections.Generic.List<CustomAttributeTypedArgument>();
            _namedArguments = new System.Collections.Generic.List<CustomAttributeNamedArgument>();
        }

        /// <summary>Gets the type of the attribute.</summary>
        public virtual Type AttributeType => _attributeType;

        /// <summary>Gets the constructor used to instantiate the attribute.</summary>
        public virtual ConstructorInfo? Constructor => _constructor;

        /// <summary>Gets the list of positional arguments specified for the attribute instance.</summary>
        public virtual System.Collections.Generic.IList<CustomAttributeTypedArgument> ConstructorArguments => _constructorArguments;

        /// <summary>Gets the list of named arguments specified for the attribute instance.</summary>
        public virtual System.Collections.Generic.IList<CustomAttributeNamedArgument> NamedArguments => _namedArguments;

        /// <summary>Returns a string representation of the custom attribute.</summary>
        public override string ToString() => $"[{AttributeType.Name}(...)]";
    }

    /// <summary>
    /// Represents an argument of a custom attribute in the reflection-only context.
    /// </summary>
    public readonly struct CustomAttributeTypedArgument
    {
        private readonly Type _argumentType;
        private readonly object? _value;

        /// <summary>Initializes a new instance of CustomAttributeTypedArgument.</summary>
        public CustomAttributeTypedArgument(Type argumentType, object? value)
        {
            _argumentType = argumentType ?? throw new ArgumentNullException(nameof(argumentType));
            _value = value;
        }

        /// <summary>Initializes a new instance with only a value.</summary>
        public CustomAttributeTypedArgument(object value)
        {
            _value = value;
            _argumentType = value?.GetType() ?? typeof(object);
        }

        /// <summary>Gets the type of the argument or of the array element.</summary>
        public Type ArgumentType => _argumentType;

        /// <summary>Gets the value of the argument.</summary>
        public object? Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is CustomAttributeTypedArgument other)
                return _argumentType == other._argumentType && Equals(_value, other._value);
            return false;
        }

        public override int GetHashCode() => (_argumentType?.GetHashCode() ?? 0) ^ (_value?.GetHashCode() ?? 0);
        public override string ToString() => _value?.ToString() ?? "null";

        public static bool operator ==(CustomAttributeTypedArgument left, CustomAttributeTypedArgument right) => left.Equals(right);
        public static bool operator !=(CustomAttributeTypedArgument left, CustomAttributeTypedArgument right) => !left.Equals(right);
    }

    /// <summary>
    /// Represents a named argument of a custom attribute in the reflection-only context.
    /// </summary>
    public readonly struct CustomAttributeNamedArgument
    {
        private readonly MemberInfo _memberInfo;
        private readonly CustomAttributeTypedArgument _typedValue;
        private readonly bool _isField;

        /// <summary>Initializes a new instance of CustomAttributeNamedArgument.</summary>
        public CustomAttributeNamedArgument(MemberInfo memberInfo, object? value)
        {
            _memberInfo = memberInfo ?? throw new ArgumentNullException(nameof(memberInfo));
            _typedValue = new CustomAttributeTypedArgument(value ?? new object());
            _isField = memberInfo.MemberType == MemberTypes.Field;
        }

        /// <summary>Initializes a new instance with a typed value.</summary>
        public CustomAttributeNamedArgument(MemberInfo memberInfo, CustomAttributeTypedArgument typedValue)
        {
            _memberInfo = memberInfo ?? throw new ArgumentNullException(nameof(memberInfo));
            _typedValue = typedValue;
            _isField = memberInfo.MemberType == MemberTypes.Field;
        }

        /// <summary>Gets the name of the attribute member.</summary>
        public string MemberName => _memberInfo.Name;

        /// <summary>Gets the attribute member.</summary>
        public MemberInfo MemberInfo => _memberInfo;

        /// <summary>Gets the typed value of the argument.</summary>
        public CustomAttributeTypedArgument TypedValue => _typedValue;

        /// <summary>Gets a value that indicates whether the named argument is a field.</summary>
        public bool IsField => _isField;

        public override bool Equals(object? obj)
        {
            if (obj is CustomAttributeNamedArgument other)
                return _memberInfo == other._memberInfo && _typedValue.Equals(other._typedValue);
            return false;
        }

        public override int GetHashCode() => _memberInfo.GetHashCode() ^ _typedValue.GetHashCode();
        public override string ToString() => $"{MemberName} = {_typedValue}";

        public static bool operator ==(CustomAttributeNamedArgument left, CustomAttributeNamedArgument right) => left.Equals(right);
        public static bool operator !=(CustomAttributeNamedArgument left, CustomAttributeNamedArgument right) => !left.Equals(right);
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
