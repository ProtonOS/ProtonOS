// ProtonOS System.Runtime - MethodInfo
// Discovers the attributes of a method and provides access to method metadata.

namespace System.Reflection
{
    /// <summary>
    /// Discovers the attributes of a method and provides access to method metadata.
    /// </summary>
    public abstract class MethodInfo : MethodBase
    {
        /// <summary>Gets a MemberTypes value indicating that this member is a method.</summary>
        public override MemberTypes MemberType => MemberTypes.Method;

        /// <summary>Gets the return parameter of the method.</summary>
        public virtual ParameterInfo? ReturnParameter => null;

        /// <summary>Gets the return type of this method.</summary>
        public virtual Type ReturnType => typeof(void);

        /// <summary>Gets the custom attributes for the return type.</summary>
        public virtual ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();

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

        /// <summary>Returns the MethodInfo object for the method on the direct or indirect base class in which the method represented by this instance was first declared.</summary>
        public virtual MethodInfo GetBaseDefinition()
        {
            return this;
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

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            return obj is MethodInfo m && m.MethodHandle.Equals(MethodHandle);
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return MethodHandle.GetHashCode();
        }

        /// <summary>Indicates whether two MethodInfo objects are equal.</summary>
        public static bool operator ==(MethodInfo? left, MethodInfo? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two MethodInfo objects are not equal.</summary>
        public static bool operator !=(MethodInfo? left, MethodInfo? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Discovers the attributes of a class constructor and provides access to constructor metadata.
    /// </summary>
    public abstract class ConstructorInfo : MethodBase
    {
        /// <summary>Represents the name of the class constructor method as it is stored in metadata.</summary>
        public static readonly string ConstructorName = ".ctor";

        /// <summary>Represents the name of the type constructor method as it is stored in metadata.</summary>
        public static readonly string TypeConstructorName = ".cctor";

        /// <summary>Gets a MemberTypes value indicating that this member is a constructor.</summary>
        public override MemberTypes MemberType => MemberTypes.Constructor;

        /// <summary>Invokes the constructor reflected by the instance that has the specified parameters, providing default values for the parameters not commonly used.</summary>
        public object Invoke(object?[]? parameters)
        {
            return Invoke(BindingFlags.Default, null, parameters, null)!;
        }

        /// <summary>When implemented in a derived class, invokes the constructor reflected by this ConstructorInfo with the specified arguments.</summary>
        public abstract object Invoke(BindingFlags invokeAttr, Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture);

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            return obj is ConstructorInfo c && c.MethodHandle.Equals(MethodHandle);
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return MethodHandle.GetHashCode();
        }

        /// <summary>Indicates whether two ConstructorInfo objects are equal.</summary>
        public static bool operator ==(ConstructorInfo? left, ConstructorInfo? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two ConstructorInfo objects are not equal.</summary>
        public static bool operator !=(ConstructorInfo? left, ConstructorInfo? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Discovers the attributes of a field and provides access to field metadata.
    /// </summary>
    public abstract class FieldInfo : MemberInfo
    {
        /// <summary>Gets a MemberTypes value indicating that this member is a field.</summary>
        public override MemberTypes MemberType => MemberTypes.Field;

        /// <summary>Gets the attributes associated with this field.</summary>
        public abstract FieldAttributes Attributes { get; }

        /// <summary>Gets the type of this field object.</summary>
        public abstract Type FieldType { get; }

        /// <summary>Gets a RuntimeFieldHandle, which is a handle to the internal metadata representation of a field.</summary>
        public abstract RuntimeFieldHandle FieldHandle { get; }

        /// <summary>Gets a value indicating whether the potential visibility of this field is described by FieldAttributes.Assembly.</summary>
        public bool IsAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Assembly;

        /// <summary>Gets a value indicating whether the potential visibility of this field is described by FieldAttributes.Family.</summary>
        public bool IsFamily => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Family;

        /// <summary>Gets a value indicating whether the potential visibility of this field is described by FieldAttributes.FamANDAssem.</summary>
        public bool IsFamilyAndAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.FamANDAssem;

        /// <summary>Gets a value indicating whether the potential visibility of this field is described by FieldAttributes.FamORAssem.</summary>
        public bool IsFamilyOrAssembly => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.FamORAssem;

        /// <summary>Gets a value indicating whether the field can only be set in the body of the constructor.</summary>
        public bool IsInitOnly => (Attributes & FieldAttributes.InitOnly) != 0;

        /// <summary>Gets a value indicating whether the value is written at compile time.</summary>
        public bool IsLiteral => (Attributes & FieldAttributes.Literal) != 0;

        /// <summary>Gets a value indicating whether this field is private.</summary>
        public bool IsPrivate => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private;

        /// <summary>Gets a value indicating whether the field is public.</summary>
        public bool IsPublic => (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;

        /// <summary>Gets a value indicating whether the corresponding SpecialName attribute is set.</summary>
        public bool IsSpecialName => (Attributes & FieldAttributes.SpecialName) != 0;

        /// <summary>Gets a value indicating whether the field is static.</summary>
        public bool IsStatic => (Attributes & FieldAttributes.Static) != 0;

        /// <summary>Gets a value indicating whether this field has the NotSerialized attribute.</summary>
        public bool IsNotSerialized => (Attributes & FieldAttributes.NotSerialized) != 0;

        /// <summary>Gets a value indicating whether this field is pinned in memory.</summary>
        public virtual bool IsPinvokeImpl => (Attributes & FieldAttributes.PinvokeImpl) != 0;

        /// <summary>Gets a value that indicates whether the current field is security-critical or security-safe-critical.</summary>
        public virtual bool IsSecurityCritical => true;

        /// <summary>Gets a value that indicates whether the current field is security-safe-critical.</summary>
        public virtual bool IsSecuritySafeCritical => false;

        /// <summary>Gets a value that indicates whether the current field is transparent at the current trust level.</summary>
        public virtual bool IsSecurityTransparent => false;

        /// <summary>Returns the value of a field supported by a given object.</summary>
        public abstract object? GetValue(object? obj);

        /// <summary>Sets the value of the field supported by the given object.</summary>
        public abstract void SetValue(object? obj, object? value);

        /// <summary>Sets the value of the field supported by the given object.</summary>
        public virtual void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder, System.Globalization.CultureInfo? culture)
        {
            SetValue(obj, value);
        }

        /// <summary>Returns a literal value associated with the field by a compiler.</summary>
        public virtual object? GetRawConstantValue()
        {
            throw new NotSupportedException();
        }

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            return obj is FieldInfo f && f.FieldHandle.Equals(FieldHandle);
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return FieldHandle.GetHashCode();
        }

        /// <summary>Indicates whether two FieldInfo objects are equal.</summary>
        public static bool operator ==(FieldInfo? left, FieldInfo? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two FieldInfo objects are not equal.</summary>
        public static bool operator !=(FieldInfo? left, FieldInfo? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Discovers the attributes of a property and provides access to property metadata.
    /// </summary>
    public abstract class PropertyInfo : MemberInfo
    {
        /// <summary>Gets a MemberTypes value indicating that this member is a property.</summary>
        public override MemberTypes MemberType => MemberTypes.Property;

        /// <summary>Gets the attributes for this property.</summary>
        public abstract PropertyAttributes Attributes { get; }

        /// <summary>Gets the type of this property.</summary>
        public abstract Type PropertyType { get; }

        /// <summary>Gets a value indicating whether the property can be read.</summary>
        public abstract bool CanRead { get; }

        /// <summary>Gets a value indicating whether the property can be written to.</summary>
        public abstract bool CanWrite { get; }

        /// <summary>Gets the get accessor for this property.</summary>
        public abstract MethodInfo? GetGetMethod(bool nonPublic);

        /// <summary>Gets the public get accessor for this property.</summary>
        public MethodInfo? GetGetMethod()
        {
            return GetGetMethod(false);
        }

        /// <summary>Gets the set accessor for this property.</summary>
        public abstract MethodInfo? GetSetMethod(bool nonPublic);

        /// <summary>Gets the public set accessor for this property.</summary>
        public MethodInfo? GetSetMethod()
        {
            return GetSetMethod(false);
        }

        /// <summary>Returns an array whose elements reflect the public get and set accessors of the property.</summary>
        public MethodInfo[] GetAccessors()
        {
            return GetAccessors(false);
        }

        /// <summary>Returns an array of the get and set accessors on this property.</summary>
        public abstract MethodInfo[] GetAccessors(bool nonPublic);

        /// <summary>Gets the get accessor for this property.</summary>
        public virtual MethodInfo? GetMethod => GetGetMethod(true);

        /// <summary>Gets the set accessor for this property.</summary>
        public virtual MethodInfo? SetMethod => GetSetMethod(true);

        /// <summary>Gets a value indicating whether the property is the special name.</summary>
        public bool IsSpecialName => (Attributes & PropertyAttributes.SpecialName) != 0;

        /// <summary>Returns the property value of a specified object with optional index values for indexed properties.</summary>
        public virtual object? GetValue(object? obj)
        {
            return GetValue(obj, null);
        }

        /// <summary>Returns the property value of a specified object with optional index values for indexed properties.</summary>
        public abstract object? GetValue(object? obj, object?[]? index);

        /// <summary>Returns the property value of a specified object with optional index values for indexed properties.</summary>
        public virtual object? GetValue(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture)
        {
            return GetValue(obj, index);
        }

        /// <summary>Sets the property value of a specified object with optional index values for indexed properties.</summary>
        public virtual void SetValue(object? obj, object? value)
        {
            SetValue(obj, value, null);
        }

        /// <summary>Sets the property value of a specified object with optional index values for indexed properties.</summary>
        public abstract void SetValue(object? obj, object? value, object?[]? index);

        /// <summary>Sets the property value of a specified object with optional index values for indexed properties.</summary>
        public virtual void SetValue(object? obj, object? value, BindingFlags invokeAttr, Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture)
        {
            SetValue(obj, value, index);
        }

        /// <summary>Returns an array of all the index parameters for the property.</summary>
        public abstract ParameterInfo[] GetIndexParameters();

        /// <summary>Returns a literal value associated with the property by a compiler.</summary>
        public virtual object? GetConstantValue()
        {
            throw new NotSupportedException();
        }

        /// <summary>Returns a literal value associated with the property by a compiler.</summary>
        public virtual object? GetRawConstantValue()
        {
            throw new NotSupportedException();
        }

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            if (obj is PropertyInfo p)
            {
                return p.DeclaringType == DeclaringType && p.Name == Name;
            }
            return false;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        /// <summary>Indicates whether two PropertyInfo objects are equal.</summary>
        public static bool operator ==(PropertyInfo? left, PropertyInfo? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two PropertyInfo objects are not equal.</summary>
        public static bool operator !=(PropertyInfo? left, PropertyInfo? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Discovers the attributes of an event and provides access to event metadata.
    /// </summary>
    public abstract class EventInfo : MemberInfo
    {
        /// <summary>Gets a MemberTypes value indicating that this member is an event.</summary>
        public override MemberTypes MemberType => MemberTypes.Event;

        /// <summary>Gets the attributes for this event.</summary>
        public abstract EventAttributes Attributes { get; }

        /// <summary>Gets the Type object of the underlying event-handler delegate associated with this event.</summary>
        public virtual Type? EventHandlerType => null;

        /// <summary>Gets the MethodInfo object for the AddEventHandler method of the event.</summary>
        public virtual MethodInfo? AddMethod => GetAddMethod(true);

        /// <summary>Gets the MethodInfo object for removing a method of the event.</summary>
        public virtual MethodInfo? RemoveMethod => GetRemoveMethod(true);

        /// <summary>Gets the method that is called when the event is raised.</summary>
        public virtual MethodInfo? RaiseMethod => GetRaiseMethod(true);

        /// <summary>Gets a value indicating whether the event is multicast.</summary>
        public virtual bool IsMulticast => true;

        /// <summary>Gets a value indicating whether the EventInfo has a name with a special meaning.</summary>
        public bool IsSpecialName => (Attributes & EventAttributes.SpecialName) != 0;

        /// <summary>Returns the method used to add an event handler delegate to the event source.</summary>
        public abstract MethodInfo? GetAddMethod(bool nonPublic);

        /// <summary>Returns the method used to remove an event handler delegate from the event source.</summary>
        public abstract MethodInfo? GetRemoveMethod(bool nonPublic);

        /// <summary>Returns the method that is called when the event is raised.</summary>
        public abstract MethodInfo? GetRaiseMethod(bool nonPublic);

        /// <summary>Returns the public method used to add an event handler delegate to the event source.</summary>
        public MethodInfo? GetAddMethod()
        {
            return GetAddMethod(false);
        }

        /// <summary>Returns the public method used to remove an event handler delegate from the event source.</summary>
        public MethodInfo? GetRemoveMethod()
        {
            return GetRemoveMethod(false);
        }

        /// <summary>Returns the method that is called when the event is raised.</summary>
        public MethodInfo? GetRaiseMethod()
        {
            return GetRaiseMethod(false);
        }

        /// <summary>Adds an event handler to an event source.</summary>
        public virtual void AddEventHandler(object? target, Delegate? handler)
        {
            var add = GetAddMethod(false);
            if (add == null)
                throw new InvalidOperationException("No add accessor.");
            add.Invoke(target, new object?[] { handler });
        }

        /// <summary>Removes an event handler from an event source.</summary>
        public virtual void RemoveEventHandler(object? target, Delegate? handler)
        {
            var remove = GetRemoveMethod(false);
            if (remove == null)
                throw new InvalidOperationException("No remove accessor.");
            remove.Invoke(target, new object?[] { handler });
        }

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            if (obj is EventInfo e)
            {
                return e.DeclaringType == DeclaringType && e.Name == Name;
            }
            return false;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }

        /// <summary>Indicates whether two EventInfo objects are equal.</summary>
        public static bool operator ==(EventInfo? left, EventInfo? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two EventInfo objects are not equal.</summary>
        public static bool operator !=(EventInfo? left, EventInfo? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Discovers the attributes of a parameter and provides access to parameter metadata.
    /// </summary>
    public class ParameterInfo : ICustomAttributeProvider
    {
        /// <summary>The attributes of the parameter.</summary>
        protected ParameterAttributes AttrsImpl;

        /// <summary>The Type of the parameter.</summary>
        protected Type? ClassImpl;

        /// <summary>The default value of the parameter.</summary>
        protected object? DefaultValueImpl;

        /// <summary>The member in which the field is implemented.</summary>
        protected MemberInfo? MemberImpl;

        /// <summary>The name of the parameter.</summary>
        protected string? NameImpl;

        /// <summary>The zero-based position of the parameter in the parameter list.</summary>
        protected int PositionImpl;

        /// <summary>Gets the attributes for this parameter.</summary>
        public virtual ParameterAttributes Attributes => AttrsImpl;

        /// <summary>Gets the member in which the parameter is implemented.</summary>
        public virtual MemberInfo Member => MemberImpl!;

        /// <summary>Gets the name of the parameter.</summary>
        public virtual string? Name => NameImpl;

        /// <summary>Gets the Type of this parameter.</summary>
        public virtual Type ParameterType => ClassImpl ?? typeof(object);

        /// <summary>Gets the zero-based position of the parameter in the formal parameter list.</summary>
        public virtual int Position => PositionImpl;

        /// <summary>Gets a value indicating whether this parameter has a default value.</summary>
        public virtual bool HasDefaultValue => (Attributes & ParameterAttributes.HasDefault) != 0;

        /// <summary>Gets a value indicating the default value if the parameter has a default value.</summary>
        public virtual object? DefaultValue => DefaultValueImpl;

        /// <summary>Gets a value indicating the default value if the parameter has a default value.</summary>
        public virtual object? RawDefaultValue => DefaultValueImpl;

        /// <summary>Gets a value indicating whether this is an input parameter.</summary>
        public bool IsIn => (Attributes & ParameterAttributes.In) != 0;

        /// <summary>Gets a value indicating whether this parameter is a locale identifier (lcid).</summary>
        public bool IsLcid => (Attributes & ParameterAttributes.Lcid) != 0;

        /// <summary>Gets a value indicating whether this parameter is optional.</summary>
        public bool IsOptional => (Attributes & ParameterAttributes.Optional) != 0;

        /// <summary>Gets a value indicating whether this is an output parameter.</summary>
        public bool IsOut => (Attributes & ParameterAttributes.Out) != 0;

        /// <summary>Gets a value indicating whether this is a Retval parameter.</summary>
        public bool IsRetval => (Attributes & ParameterAttributes.Retval) != 0;

        /// <summary>Gets a value that identifies this parameter in metadata.</summary>
        public virtual int MetadataToken => 0;

        /// <summary>Returns an array of all custom attributes applied to this parameter.</summary>
        public virtual object[] GetCustomAttributes(bool inherit)
        {
            return Array.Empty<object>();
        }

        /// <summary>Returns an array of custom attributes applied to this parameter and identified by Type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Array.Empty<object>();
        }

        /// <summary>Indicates whether one or more instances of the specified attribute type or derived types is applied to this parameter.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return false;
        }

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString()
        {
            return $"{ParameterType?.Name ?? "Unknown"} {Name ?? "?"}";
        }
    }
}
