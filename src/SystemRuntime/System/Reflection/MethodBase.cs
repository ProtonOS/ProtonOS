// ProtonOS System.Runtime - MethodBase
// Provides information about methods and constructors.

using System.Collections.Generic;

namespace System.Reflection
{
    /// <summary>
    /// Provides information about methods and constructors.
    /// </summary>
    public abstract class MethodBase : MemberInfo
    {
        /// <summary>Gets the attributes associated with this method.</summary>
        public abstract MethodAttributes Attributes { get; }

        /// <summary>Gets the calling conventions for this method.</summary>
        public virtual CallingConventions CallingConvention => CallingConventions.Standard;

        /// <summary>Gets a value indicating whether the method is abstract.</summary>
        public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;

        /// <summary>Gets a value indicating whether the potential visibility of this method is described by MethodAttributes.Assembly.</summary>
        public bool IsAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly;

        /// <summary>Gets a value indicating whether the visibility of this method is described by MethodAttributes.Family.</summary>
        public bool IsFamily => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family;

        /// <summary>Gets a value indicating whether the visibility of this method is described by MethodAttributes.FamANDAssem.</summary>
        public bool IsFamilyAndAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamANDAssem;

        /// <summary>Gets a value indicating whether the visibility of this method is described by MethodAttributes.FamORAssem.</summary>
        public bool IsFamilyOrAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem;

        /// <summary>Gets a value indicating whether this method is final.</summary>
        public bool IsFinal => (Attributes & MethodAttributes.Final) != 0;

        /// <summary>Gets a value indicating whether only a member of the same kind with exactly the same signature is hidden in the derived class.</summary>
        public bool IsHideBySig => (Attributes & MethodAttributes.HideBySig) != 0;

        /// <summary>Gets a value indicating whether this member is private.</summary>
        public bool IsPrivate => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;

        /// <summary>Gets a value indicating whether this is a public method.</summary>
        public bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

        /// <summary>Gets a value indicating whether this method has a special name.</summary>
        public bool IsSpecialName => (Attributes & MethodAttributes.SpecialName) != 0;

        /// <summary>Gets a value indicating whether the method is static.</summary>
        public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;

        /// <summary>Gets a value indicating whether the method is virtual.</summary>
        public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;

        /// <summary>Gets a value indicating whether the generic method contains unassigned generic type parameters.</summary>
        public virtual bool ContainsGenericParameters => false;

        /// <summary>Gets a value indicating whether the method is a generic method.</summary>
        public virtual bool IsGenericMethod => false;

        /// <summary>Gets a value indicating whether the method is a generic method definition.</summary>
        public virtual bool IsGenericMethodDefinition => false;

        /// <summary>Gets a value that indicates whether the current method or constructor is security-critical or security-safe-critical.</summary>
        public virtual bool IsSecurityCritical => true;

        /// <summary>Gets a value that indicates whether the current method or constructor is security-safe-critical.</summary>
        public virtual bool IsSecuritySafeCritical => false;

        /// <summary>Gets a value that indicates whether the current method or constructor is transparent at the current trust level.</summary>
        public virtual bool IsSecurityTransparent => false;

        /// <summary>Gets a value indicating whether this is a constructor.</summary>
        public bool IsConstructor => this is ConstructorInfo;

        /// <summary>Gets a handle to the internal metadata representation of a method.</summary>
        public abstract RuntimeMethodHandle MethodHandle { get; }

        /// <summary>Gets the MethodImplAttributes flags that specify the attributes of a method implementation.</summary>
        public virtual MethodImplAttributes MethodImplementationFlags => MethodImplAttributes.IL;

        /// <summary>Gets the parameters of the specified method or constructor.</summary>
        public abstract ParameterInfo[] GetParameters();

        /// <summary>Returns an array of Type objects that represent the type arguments of a generic method or the type parameters of a generic method definition.</summary>
        public virtual Type[] GetGenericArguments()
        {
            throw new NotSupportedException("This method is not generic.");
        }

        /// <summary>Invokes the method or constructor represented by the current instance.</summary>
        public object? Invoke(object? obj, object?[]? parameters)
        {
            return Invoke(obj, BindingFlags.Default, null, parameters, null);
        }

        /// <summary>Invokes the method or constructor represented by the current instance with the specified parameters.</summary>
        public abstract object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture);

        /// <summary>Returns the MethodBody object that provides access to the MSIL stream, local variables, and exceptions for the current method.</summary>
        public virtual MethodBody? GetMethodBody()
        {
            return null;
        }
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

        /// <summary>Specifies that the method implementation is in optimized intermediate language (OPTIL).</summary>
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
        public virtual IList<LocalVariableInfo> LocalVariables => Array.Empty<LocalVariableInfo>();

        /// <summary>Gets the maximum number of items on the operand stack when the method is executing.</summary>
        public virtual int MaxStackSize => 0;

        /// <summary>Gets a value indicating whether local variables in the method body are initialized to the default values for their types.</summary>
        public virtual bool InitLocals => true;

        /// <summary>Gets a list that includes all the exception-handling clauses in the method body.</summary>
        public virtual IList<ExceptionHandlingClause> ExceptionHandlingClauses => Array.Empty<ExceptionHandlingClause>();

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

        /// <summary>Gets a value that indicates whether the object referred to by the local variable is pinned in memory.</summary>
        public virtual bool IsPinned => false;

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString()
        {
            return $"{LocalType.Name} ({LocalIndex})";
        }
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

        /// <summary>Gets the offset within the method body, in bytes, of the user-supplied filter code.</summary>
        public virtual int FilterOffset => 0;

        /// <summary>Gets the length, in bytes, of the body of this exception-handling clause.</summary>
        public virtual int HandlerLength => 0;

        /// <summary>Gets the offset within the method body, in bytes, of this exception-handling clause.</summary>
        public virtual int HandlerOffset => 0;

        /// <summary>Gets the length, in bytes, of the body of the try block that includes this exception-handling clause.</summary>
        public virtual int TryLength => 0;

        /// <summary>Gets the offset within the method, in bytes, of the try block that includes this exception-handling clause.</summary>
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

        /// <summary>The clause is executed if an exception occurs, but not upon completion of the try block.</summary>
        Filter = 1,

        /// <summary>The clause is executed whenever the try block exits.</summary>
        Finally = 2,

        /// <summary>The clause is executed if an exception occurs, but not upon completion of normal control flow through the try block.</summary>
        Fault = 4,
    }

    /// <summary>
    /// Provides for binding in the reflection process.
    /// </summary>
    public abstract class Binder
    {
        /// <summary>Selects a method from the given set of methods, based on the argument type.</summary>
        public abstract MethodBase? BindToMethod(BindingFlags bindingAttr, MethodBase[] match, ref object?[] args, ParameterModifier[]? modifiers, System.Globalization.CultureInfo? culture, string[]? names, out object? state);

        /// <summary>Selects a field from the given set of fields, based on the specified criteria.</summary>
        public abstract FieldInfo? BindToField(BindingFlags bindingAttr, FieldInfo[] match, object value, System.Globalization.CultureInfo? culture);

        /// <summary>Selects a method to invoke from the given set of methods, based on the supplied arguments.</summary>
        public abstract MethodBase? SelectMethod(BindingFlags bindingAttr, MethodBase[] match, Type[] types, ParameterModifier[]? modifiers);

        /// <summary>Selects a property from the given set of properties, based on the specified criteria.</summary>
        public abstract PropertyInfo? SelectProperty(BindingFlags bindingAttr, PropertyInfo[] match, Type? returnType, Type[]? indexes, ParameterModifier[]? modifiers);

        /// <summary>Changes the type of the given Object to the given Type.</summary>
        public abstract object ChangeType(object value, Type type, System.Globalization.CultureInfo? culture);

        /// <summary>Upon returning from BindToMethod, restores the args argument to what it was when it came from BindToMethod.</summary>
        public abstract void ReorderArgumentArray(ref object?[] args, object state);
    }

    /// <summary>
    /// Attaches a modifier to parameters so that binding can work with parameter signatures in which the types have been modified.
    /// </summary>
    public readonly struct ParameterModifier
    {
        private readonly bool[] _byRef;

        /// <summary>Initializes a new instance of the ParameterModifier structure representing the specified number of parameters.</summary>
        public ParameterModifier(int parameterCount)
        {
            if (parameterCount <= 0) throw new ArgumentException("Must be a positive number.", nameof(parameterCount));
            _byRef = new bool[parameterCount];
        }

        /// <summary>Gets or sets a value that specifies whether the parameter at the specified index position is to be modified by the current ParameterModifier.</summary>
        public bool this[int index]
        {
            get => _byRef[index];
            set => _byRef[index] = value;
        }
    }
}

namespace System.Globalization
{
    /// <summary>
    /// Provides information about a specific culture (called a locale for unmanaged code development).
    /// </summary>
    public class CultureInfo
    {
        private static CultureInfo? _currentCulture;
        private static CultureInfo? _invariantCulture;

        private readonly string _name;

        /// <summary>Gets the CultureInfo that represents the culture used by the current thread.</summary>
        public static CultureInfo CurrentCulture => _currentCulture ??= new CultureInfo("");

        /// <summary>Gets the CultureInfo object that is culture-independent (invariant).</summary>
        public static CultureInfo InvariantCulture => _invariantCulture ??= new CultureInfo("");

        /// <summary>Initializes a new instance of the CultureInfo class based on the culture specified by name.</summary>
        public CultureInfo(string name)
        {
            _name = name ?? "";
        }

        /// <summary>Gets the culture name in the format languagecode2-country/regioncode2.</summary>
        public string Name => _name;

        /// <summary>Gets the culture name in the format languagecode2-country/regioncode2.</summary>
        public string DisplayName => _name;

        /// <summary>Gets the culture identifier for the current CultureInfo.</summary>
        public virtual int LCID => 0x007F; // Invariant

        /// <summary>Returns a string representation of this object.</summary>
        public override string ToString() => _name;
    }
}
