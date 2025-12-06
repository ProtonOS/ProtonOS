// ProtonOS System.Runtime - MemberTypes
// Marks each type of member that is defined as a derived class of MemberInfo.

namespace System.Reflection
{
    /// <summary>
    /// Marks each type of member that is defined as a derived class of MemberInfo.
    /// </summary>
    [Flags]
    public enum MemberTypes
    {
        /// <summary>Specifies that the member is a constructor.</summary>
        Constructor = 1,

        /// <summary>Specifies that the member is an event.</summary>
        Event = 2,

        /// <summary>Specifies that the member is a field.</summary>
        Field = 4,

        /// <summary>Specifies that the member is a method.</summary>
        Method = 8,

        /// <summary>Specifies that the member is a property.</summary>
        Property = 16,

        /// <summary>Specifies that the member is a type.</summary>
        TypeInfo = 32,

        /// <summary>Specifies that the member is a custom member type.</summary>
        Custom = 64,

        /// <summary>Specifies that the member is a nested type.</summary>
        NestedType = 128,

        /// <summary>Specifies all member types.</summary>
        All = Constructor | Event | Field | Method | Property | TypeInfo | NestedType,
    }

    /// <summary>
    /// Identifies the types of members in a class.
    /// </summary>
    [Flags]
    public enum MethodAttributes
    {
        /// <summary>Indicates that the method is private.</summary>
        PrivateScope = 0,

        /// <summary>Indicates that the method is private.</summary>
        Private = 1,

        /// <summary>Indicates that the method is accessible to any class of this assembly.</summary>
        FamANDAssem = 2,

        /// <summary>Indicates that the method is accessible to derived classes anywhere, as well as to any class in the assembly.</summary>
        Assembly = 3,

        /// <summary>Indicates that the method is accessible only to members of this class and its derived classes.</summary>
        Family = 4,

        /// <summary>Indicates that the method is accessible to derived classes anywhere, as well as to any class in the assembly.</summary>
        FamORAssem = 5,

        /// <summary>Indicates that the method is accessible to any class anywhere.</summary>
        Public = 6,

        /// <summary>Retrieves accessibility information.</summary>
        MemberAccessMask = 7,

        /// <summary>Indicates that the method cannot be overridden.</summary>
        Final = 32,

        /// <summary>Indicates that the method is virtual.</summary>
        Virtual = 64,

        /// <summary>Indicates that the method hides by name and signature; otherwise, by name only.</summary>
        HideBySig = 128,

        /// <summary>Indicates that the method can only be overridden when it is also accessible.</summary>
        CheckAccessOnOverride = 512,

        /// <summary>Indicates that the vtable slot is reused.</summary>
        VtableLayoutMask = 256,

        /// <summary>Indicates that the vtable slot for this method is reused.</summary>
        ReuseSlot = 0,

        /// <summary>Indicates that the method always gets a new slot in the vtable.</summary>
        NewSlot = 256,

        /// <summary>Indicates that the method is abstract.</summary>
        Abstract = 1024,

        /// <summary>Indicates that the method is special.</summary>
        SpecialName = 2048,

        /// <summary>Indicates a reserved flag for runtime use only.</summary>
        RTSpecialName = 4096,

        /// <summary>Indicates that the implementation is forwarded through PInvoke.</summary>
        PinvokeImpl = 8192,

        /// <summary>Indicates that the managed method is exported by thunk to unmanaged code.</summary>
        UnmanagedExport = 8,

        /// <summary>Indicates that the common language runtime checks the name encoding.</summary>
        HasSecurity = 16384,

        /// <summary>Indicates that the method calls another method containing security code.</summary>
        RequireSecObject = 32768,

        /// <summary>Indicates that the method is static.</summary>
        Static = 16,
    }

    /// <summary>
    /// Specifies field attributes.
    /// </summary>
    [Flags]
    public enum FieldAttributes
    {
        /// <summary>Specifies that the field cannot be referenced.</summary>
        PrivateScope = 0,

        /// <summary>Specifies that the field is accessible only by the parent type.</summary>
        Private = 1,

        /// <summary>Specifies that the field is accessible only by sub-types in this assembly.</summary>
        FamANDAssem = 2,

        /// <summary>Specifies that the field is accessible throughout the assembly.</summary>
        Assembly = 3,

        /// <summary>Specifies that the field is accessible only by type and sub-types.</summary>
        Family = 4,

        /// <summary>Specifies that the field is accessible by sub-types anywhere, as well as throughout this assembly.</summary>
        FamORAssem = 5,

        /// <summary>Specifies that the field is accessible by any member.</summary>
        Public = 6,

        /// <summary>Specifies the access level of a given field.</summary>
        FieldAccessMask = 7,

        /// <summary>Specifies that the field represents the defined type, or else it is per-instance.</summary>
        Static = 16,

        /// <summary>Specifies that the field is initialized only.</summary>
        InitOnly = 32,

        /// <summary>Specifies that the field's value is a compile-time constant.</summary>
        Literal = 64,

        /// <summary>Specifies that the field does not have to be serialized when the type is remoted.</summary>
        NotSerialized = 128,

        /// <summary>Specifies that the field has a special name.</summary>
        SpecialName = 512,

        /// <summary>Specifies that the common language runtime metadata internal APIs check the name encoding.</summary>
        RTSpecialName = 1024,

        /// <summary>Specifies that the field has marshaling information.</summary>
        HasFieldMarshal = 4096,

        /// <summary>Specifies that the field has a default value.</summary>
        HasDefault = 32768,

        /// <summary>Specifies that the field has a Relative Virtual Address (RVA).</summary>
        HasFieldRVA = 256,

        /// <summary>Reserved for future use.</summary>
        ReservedMask = 38144,

        /// <summary>Indicates a reserved flag for runtime use only.</summary>
        PinvokeImpl = 8192,
    }

    /// <summary>
    /// Specifies type attributes.
    /// </summary>
    [Flags]
    public enum TypeAttributes
    {
        /// <summary>Specifies that the class is not public.</summary>
        NotPublic = 0,

        /// <summary>Specifies that the class is public.</summary>
        Public = 1,

        /// <summary>Specifies that the class is nested with public visibility.</summary>
        NestedPublic = 2,

        /// <summary>Specifies that the class is nested with private visibility.</summary>
        NestedPrivate = 3,

        /// <summary>Specifies that the class is nested with family visibility.</summary>
        NestedFamily = 4,

        /// <summary>Specifies that the class is nested with assembly visibility.</summary>
        NestedAssembly = 5,

        /// <summary>Specifies that the class is nested with assembly and family visibility.</summary>
        NestedFamANDAssem = 6,

        /// <summary>Specifies that the class is nested with family or assembly visibility.</summary>
        NestedFamORAssem = 7,

        /// <summary>Specifies type visibility information.</summary>
        VisibilityMask = 7,

        /// <summary>Specifies class semantics information; the current class is contextful (else agile).</summary>
        LayoutMask = 24,

        /// <summary>Specifies that class fields are auto-laid out by the common language runtime.</summary>
        AutoLayout = 0,

        /// <summary>Specifies that class fields are laid out sequentially.</summary>
        SequentialLayout = 8,

        /// <summary>Specifies that class fields are laid out at specified offsets.</summary>
        ExplicitLayout = 16,

        /// <summary>Specifies class semantics information.</summary>
        ClassSemanticsMask = 32,

        /// <summary>Specifies that the type is a class.</summary>
        Class = 0,

        /// <summary>Specifies that the type is an interface.</summary>
        Interface = 32,

        /// <summary>Specifies that the type is abstract.</summary>
        Abstract = 128,

        /// <summary>Specifies that the class is sealed.</summary>
        Sealed = 256,

        /// <summary>Specifies that the class is special.</summary>
        SpecialName = 1024,

        /// <summary>Specifies that the class or interface is imported from another module.</summary>
        Import = 4096,

        /// <summary>Specifies that the class can be serialized.</summary>
        Serializable = 8192,

        /// <summary>Specifies that the type is a Windows Runtime type.</summary>
        WindowsRuntime = 16384,

        /// <summary>Used to retrieve string information for native interoperability.</summary>
        StringFormatMask = 196608,

        /// <summary>LPTSTR is interpreted as ANSI.</summary>
        AnsiClass = 0,

        /// <summary>LPTSTR is interpreted as Unicode.</summary>
        UnicodeClass = 65536,

        /// <summary>LPTSTR is interpreted automatically.</summary>
        AutoClass = 131072,

        /// <summary>A non-standard encoding specified by CustomFormatMask.</summary>
        CustomFormatClass = 196608,

        /// <summary>Used to retrieve non-standard encoding information for native interop.</summary>
        CustomFormatMask = 12582912,

        /// <summary>Specifies that calling static methods of the type does not force the system to initialize the type.</summary>
        BeforeFieldInit = 1048576,

        /// <summary>Runtime should check name encoding.</summary>
        RTSpecialName = 2048,

        /// <summary>Type has security associate with it.</summary>
        HasSecurity = 262144,

        /// <summary>This class is used for remoting.</summary>
        IsTypeForwarder = 2097152,

        /// <summary>Type is reserved.</summary>
        ReservedMask = 264192,
    }

    /// <summary>
    /// Specifies property attributes.
    /// </summary>
    [Flags]
    public enum PropertyAttributes
    {
        /// <summary>Specifies that no attributes are associated with a property.</summary>
        None = 0,

        /// <summary>Specifies that the property is special, with the name describing how the property is special.</summary>
        SpecialName = 512,

        /// <summary>Specifies that the metadata internal APIs check the name encoding.</summary>
        RTSpecialName = 1024,

        /// <summary>Specifies that the property has a default value.</summary>
        HasDefault = 4096,

        /// <summary>Reserved for future use.</summary>
        Reserved2 = 8192,

        /// <summary>Reserved for future use.</summary>
        Reserved3 = 16384,

        /// <summary>Reserved for future use.</summary>
        Reserved4 = 32768,

        /// <summary>Specifies a flag reserved for runtime use only.</summary>
        ReservedMask = 62464,
    }

    /// <summary>
    /// Specifies event attributes.
    /// </summary>
    [Flags]
    public enum EventAttributes
    {
        /// <summary>Specifies that the event has no attributes.</summary>
        None = 0,

        /// <summary>Specifies that the event is special.</summary>
        SpecialName = 512,

        /// <summary>Specifies that the common language runtime should check name encoding.</summary>
        RTSpecialName = 1024,

        /// <summary>Specifies a reserved flag for runtime use only.</summary>
        ReservedMask = 1024,
    }

    /// <summary>
    /// Specifies parameter attributes.
    /// </summary>
    [Flags]
    public enum ParameterAttributes
    {
        /// <summary>Specifies that there is no parameter attribute.</summary>
        None = 0,

        /// <summary>Specifies that the parameter is an input parameter.</summary>
        In = 1,

        /// <summary>Specifies that the parameter is an output parameter.</summary>
        Out = 2,

        /// <summary>Specifies that the parameter is a locale identifier.</summary>
        Lcid = 4,

        /// <summary>Specifies that the parameter is a return value.</summary>
        Retval = 8,

        /// <summary>Specifies that the parameter is optional.</summary>
        Optional = 16,

        /// <summary>Specifies that the parameter has a default value.</summary>
        HasDefault = 4096,

        /// <summary>Specifies that the parameter has field marshaling information.</summary>
        HasFieldMarshal = 8192,

        /// <summary>Reserved for future use.</summary>
        Reserved3 = 16384,

        /// <summary>Reserved for future use.</summary>
        Reserved4 = 32768,

        /// <summary>Specifies that the parameter is reserved.</summary>
        ReservedMask = 61440,
    }

    /// <summary>
    /// Specifies how generic type parameters are to be used.
    /// </summary>
    [Flags]
    public enum GenericParameterAttributes
    {
        /// <summary>There are no special flags.</summary>
        None = 0,

        /// <summary>Selects the combination of all variance flags.</summary>
        VarianceMask = 3,

        /// <summary>The generic type parameter is covariant.</summary>
        Covariant = 1,

        /// <summary>The generic type parameter is contravariant.</summary>
        Contravariant = 2,

        /// <summary>Selects the combination of all special constraint flags.</summary>
        SpecialConstraintMask = 28,

        /// <summary>A type can be substituted for the generic type parameter only if it is a reference type.</summary>
        ReferenceTypeConstraint = 4,

        /// <summary>A type can be substituted for the generic type parameter only if it is a value type and is not nullable.</summary>
        NotNullableValueTypeConstraint = 8,

        /// <summary>A type can be substituted for the generic type parameter only if it has a parameterless constructor.</summary>
        DefaultConstructorConstraint = 16,
    }

    /// <summary>
    /// Specifies the calling convention for a method.
    /// </summary>
    [Flags]
    public enum CallingConventions
    {
        /// <summary>Specifies the default calling convention as determined by the common language runtime.</summary>
        Standard = 1,

        /// <summary>Specifies the calling convention for methods with variable arguments.</summary>
        VarArgs = 2,

        /// <summary>Specifies that either the Standard or VarArgs calling convention may be used.</summary>
        Any = 3,

        /// <summary>Specifies an instance or virtual method (not a static method).</summary>
        HasThis = 32,

        /// <summary>Specifies that the signature is a function-pointer signature.</summary>
        ExplicitThis = 64,
    }
}
