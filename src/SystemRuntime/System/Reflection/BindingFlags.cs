// ProtonOS System.Runtime - BindingFlags
// Specifies flags that control binding and the way in which the search for members and types is conducted by reflection.

namespace System.Reflection
{
    /// <summary>
    /// Specifies flags that control binding and the way in which the search for members and types is conducted by reflection.
    /// </summary>
    [Flags]
    public enum BindingFlags
    {
        /// <summary>Specifies that no binding flags are defined.</summary>
        Default = 0,

        /// <summary>Specifies that the case of the member name should not be considered when binding.</summary>
        IgnoreCase = 1,

        /// <summary>Specifies that only members declared at the level of the supplied type's hierarchy should be considered.</summary>
        DeclaredOnly = 2,

        /// <summary>Specifies that instance members are to be included in the search.</summary>
        Instance = 4,

        /// <summary>Specifies that static members are to be included in the search.</summary>
        Static = 8,

        /// <summary>Specifies that public members are to be included in the search.</summary>
        Public = 16,

        /// <summary>Specifies that non-public members are to be included in the search.</summary>
        NonPublic = 32,

        /// <summary>Specifies that public and protected static members up the hierarchy should be returned.</summary>
        FlattenHierarchy = 64,

        /// <summary>Specifies that a method is to be invoked.</summary>
        InvokeMethod = 256,

        /// <summary>Specifies that Reflection should create an instance of the specified type.</summary>
        CreateInstance = 512,

        /// <summary>Specifies that the value of the specified field should be returned.</summary>
        GetField = 1024,

        /// <summary>Specifies that the value of the specified field should be set.</summary>
        SetField = 2048,

        /// <summary>Specifies that the value of the specified property should be returned.</summary>
        GetProperty = 4096,

        /// <summary>Specifies that the value of the specified property should be set.</summary>
        SetProperty = 8192,

        /// <summary>Specifies that types of the supplied arguments must exactly match the types of the corresponding formal parameters.</summary>
        ExactBinding = 65536,

        /// <summary>Not implemented.</summary>
        SuppressChangeType = 131072,

        /// <summary>Returns the set of members whose parameter count matches the number of supplied arguments.</summary>
        OptionalParamBinding = 262144,

        /// <summary>Used in COM interop to specify that the return value of the member can be ignored.</summary>
        IgnoreReturn = 16777216,

        /// <summary>Specifies that the search should consider public and non-public instance members.</summary>
        DoNotWrapExceptions = 33554432,
    }
}
