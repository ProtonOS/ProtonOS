// ProtonOS System.Runtime - MemberInfo
// Base class for all reflection objects representing members.

using System.Collections.Generic;

namespace System.Reflection
{
    /// <summary>
    /// Obtains information about the attributes of a member and provides access to member metadata.
    /// </summary>
    public abstract class MemberInfo : ICustomAttributeProvider
    {
        /// <summary>Gets the class that declares this member.</summary>
        public abstract Type? DeclaringType { get; }

        /// <summary>Gets a MemberTypes value indicating the type of the member.</summary>
        public abstract MemberTypes MemberType { get; }

        /// <summary>Gets the name of the current member.</summary>
        public abstract string Name { get; }

        /// <summary>Gets the class object that was used to obtain this member.</summary>
        public virtual Type? ReflectedType => DeclaringType;

        /// <summary>Gets a value that identifies a metadata element.</summary>
        public virtual int MetadataToken => 0;

        /// <summary>Gets the module in which the type that declares the member is defined.</summary>
        public virtual Module Module => throw new NotImplementedException();

        /// <summary>Returns an array of all custom attributes applied to this member.</summary>
        public virtual object[] GetCustomAttributes(bool inherit)
        {
            return Array.Empty<object>();
        }

        /// <summary>Returns an array of custom attributes applied to this member and identified by Type.</summary>
        public virtual object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return Array.Empty<object>();
        }

        /// <summary>Indicates whether one or more attributes of the specified type or derived types is applied to this member.</summary>
        public virtual bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null) throw new ArgumentNullException(nameof(attributeType));
            return false;
        }

        /// <summary>Returns a list of CustomAttributeData objects representing data about the attributes.</summary>
        public virtual IList<CustomAttributeData> GetCustomAttributesData()
        {
            return Array.Empty<CustomAttributeData>();
        }

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            return obj is MemberInfo;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>Indicates whether two MemberInfo objects are equal.</summary>
        public static bool operator ==(MemberInfo? left, MemberInfo? right)
        {
            if (left is null) return right is null;
            return left.Equals(right);
        }

        /// <summary>Indicates whether two MemberInfo objects are not equal.</summary>
        public static bool operator !=(MemberInfo? left, MemberInfo? right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Provides access to custom attribute data.
    /// </summary>
    public interface ICustomAttributeProvider
    {
        /// <summary>Returns an array of all custom attributes defined on this member.</summary>
        object[] GetCustomAttributes(bool inherit);

        /// <summary>Returns an array of custom attributes defined on this member, identified by type.</summary>
        object[] GetCustomAttributes(Type attributeType, bool inherit);

        /// <summary>Indicates whether one or more instance of attributeType is defined on this member.</summary>
        bool IsDefined(Type attributeType, bool inherit);
    }

    /// <summary>
    /// Provides information about custom attribute data.
    /// </summary>
    public class CustomAttributeData
    {
        private readonly Type _attributeType;
        private readonly ConstructorInfo? _constructor;
        private readonly IList<CustomAttributeTypedArgument> _constructorArguments;
        private readonly IList<CustomAttributeNamedArgument> _namedArguments;

        /// <summary>Initializes a new instance of CustomAttributeData.</summary>
        protected CustomAttributeData()
        {
            _attributeType = typeof(Attribute);
            _constructorArguments = Array.Empty<CustomAttributeTypedArgument>();
            _namedArguments = Array.Empty<CustomAttributeNamedArgument>();
        }

        /// <summary>Gets the type of the attribute.</summary>
        public virtual Type AttributeType => _attributeType;

        /// <summary>Gets the constructor used to instantiate the attribute.</summary>
        public virtual ConstructorInfo? Constructor => _constructor;

        /// <summary>Gets the list of positional arguments specified for the attribute instance.</summary>
        public virtual IList<CustomAttributeTypedArgument> ConstructorArguments => _constructorArguments;

        /// <summary>Gets the list of named arguments specified for the attribute instance.</summary>
        public virtual IList<CustomAttributeNamedArgument> NamedArguments => _namedArguments;

        /// <summary>Returns a string representation of the custom attribute.</summary>
        public override string ToString()
        {
            return $"[{AttributeType.Name}(...)]";
        }
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

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            if (obj is CustomAttributeTypedArgument other)
            {
                return _argumentType == other._argumentType && Equals(_value, other._value);
            }
            return false;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return (_argumentType?.GetHashCode() ?? 0) ^ (_value?.GetHashCode() ?? 0);
        }

        /// <summary>Returns a string representation of the typed argument.</summary>
        public override string ToString()
        {
            return _value?.ToString() ?? "null";
        }

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

        /// <summary>Returns a value that indicates whether this instance is equal to a specified object.</summary>
        public override bool Equals(object? obj)
        {
            if (obj is CustomAttributeNamedArgument other)
            {
                return _memberInfo == other._memberInfo && _typedValue.Equals(other._typedValue);
            }
            return false;
        }

        /// <summary>Returns the hash code for this instance.</summary>
        public override int GetHashCode()
        {
            return _memberInfo.GetHashCode() ^ _typedValue.GetHashCode();
        }

        /// <summary>Returns a string representation of the named argument.</summary>
        public override string ToString()
        {
            return $"{MemberName} = {_typedValue}";
        }

        public static bool operator ==(CustomAttributeNamedArgument left, CustomAttributeNamedArgument right) => left.Equals(right);
        public static bool operator !=(CustomAttributeNamedArgument left, CustomAttributeNamedArgument right) => !left.Equals(right);
    }
}
