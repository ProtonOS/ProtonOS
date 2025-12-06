// ProtonOS System.Runtime - StringComparison enum

namespace System
{
    /// <summary>
    /// Specifies the culture, case, and sort rules to be used by certain overloads of string methods.
    /// </summary>
    public enum StringComparison
    {
        /// <summary>Compare strings using culture-sensitive sort rules and the current culture.</summary>
        CurrentCulture = 0,
        /// <summary>Compare strings using culture-sensitive sort rules, the current culture, and ignoring case.</summary>
        CurrentCultureIgnoreCase = 1,
        /// <summary>Compare strings using culture-sensitive sort rules and the invariant culture.</summary>
        InvariantCulture = 2,
        /// <summary>Compare strings using culture-sensitive sort rules, the invariant culture, and ignoring case.</summary>
        InvariantCultureIgnoreCase = 3,
        /// <summary>Compare strings using ordinal (binary) sort rules.</summary>
        Ordinal = 4,
        /// <summary>Compare strings using ordinal (binary) sort rules and ignoring case.</summary>
        OrdinalIgnoreCase = 5
    }
}
