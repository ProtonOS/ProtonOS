// ProtonOS kernel - AOT Method Registry
// Provides lookup for AOT-compiled korlib methods that can be called from JIT code.
// These methods have no JIT metadata - they're compiled directly into the kernel.

using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;

namespace ProtonOS.Runtime;

/// <summary>
/// Entry for a registered AOT method.
/// </summary>
public unsafe struct AotMethodEntry
{
    /// <summary>Type name hash (e.g., hash of "System.String").</summary>
    public uint TypeNameHash;

    /// <summary>Method name hash (e.g., hash of "get_Length").</summary>
    public uint MethodNameHash;

    /// <summary>Native code address.</summary>
    public nint NativeCode;

    /// <summary>Number of parameters (NOT including 'this' for instance methods).</summary>
    public byte ArgCount;

    /// <summary>Return type kind.</summary>
    public ReturnKind ReturnKind;

    /// <summary>Whether this is an instance method (has 'this' pointer).</summary>
    public bool HasThis;

    /// <summary>Whether this is a virtual method.</summary>
    public bool IsVirtual;
}

/// <summary>
/// Registry for AOT-compiled korlib methods that JIT code can call.
/// This allows JIT-compiled assemblies to call methods like String.get_Length
/// which are AOT-compiled into the kernel without JIT metadata.
/// </summary>
public static unsafe class AotMethodRegistry
{
    private const int MaxEntries = 128;

    private static AotMethodEntry* _entries;
    private static int _count;
    private static bool _initialized;

    /// <summary>
    /// Initialize the AOT method registry.
    /// Must be called during kernel initialization before JIT compilation.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[AotRegistry] Initializing...");

        _entries = (AotMethodEntry*)HeapAllocator.AllocZeroed(
            (ulong)(MaxEntries * sizeof(AotMethodEntry)));
        if (_entries == null)
        {
            DebugConsole.WriteLine("[AotRegistry] Failed to allocate storage");
            return;
        }

        _count = 0;

        // Register well-known String methods
        RegisterStringMethods();

        // Register well-known Object methods
        RegisterObjectMethods();

        // Register well-known Int32 methods
        RegisterInt32Methods();

        _initialized = true;
        DebugConsole.Write("[AotRegistry] Initialized with ");
        DebugConsole.WriteDecimal(_count);
        DebugConsole.WriteLine(" methods");
    }

    /// <summary>
    /// Register String methods with their wrapper addresses.
    /// </summary>
    private static void RegisterStringMethods()
    {
        // String.get_Length (instance property getter) - 0 parameters, HasThis=true
        Register(
            "System.String", "get_Length",
            (nint)(delegate*<string, int>)&StringHelpers.GetLength,
            0, ReturnKind.Int32, true, false);

        // String.Concat(string, string) (static method)
        Register(
            "System.String", "Concat",
            (nint)(delegate*<string?, string?, string>)&StringHelpers.Concat2,
            2, ReturnKind.IntPtr, false, false);

        // String.Concat(string, string, string) (static method)
        Register(
            "System.String", "Concat",
            (nint)(delegate*<string?, string?, string?, string>)&StringHelpers.Concat3,
            3, ReturnKind.IntPtr, false, false);

        // String.get_Chars (indexer getter) - 1 int parameter, HasThis=true
        Register(
            "System.String", "get_Chars",
            (nint)(delegate*<string, int, char>)&StringHelpers.GetChars,
            1, ReturnKind.Int32, true, false);

        // String.IsNullOrEmpty (static method)
        Register(
            "System.String", "IsNullOrEmpty",
            (nint)(delegate*<string?, bool>)&StringHelpers.IsNullOrEmpty,
            1, ReturnKind.Int32, false, false);

        // String.Equals(string) - 1 string parameter, HasThis=true
        Register(
            "System.String", "Equals",
            (nint)(delegate*<string, string?, bool>)&StringHelpers.Equals,
            1, ReturnKind.Int32, true, false);
    }

    /// <summary>
    /// Register Object methods with their wrapper addresses.
    /// </summary>
    private static void RegisterObjectMethods()
    {
        // Object..ctor() - constructor, 0 parameters (but HasThis=true for instance method)
        // For constructors, we just return - the object is already allocated
        Register(
            "System.Object", ".ctor",
            (nint)(delegate*<object, void>)&ObjectHelpers.Ctor,
            0, ReturnKind.Void, true, false);

        // Object.GetHashCode() - instance method, returns int
        Register(
            "System.Object", "GetHashCode",
            (nint)(delegate*<object, int>)&ObjectHelpers.GetHashCode,
            0, ReturnKind.Int32, true, true);

        // Object.Equals(object) - instance method, 1 parameter, returns bool
        Register(
            "System.Object", "Equals",
            (nint)(delegate*<object, object?, bool>)&ObjectHelpers.Equals,
            1, ReturnKind.Int32, true, true);

        // Object.ToString() - instance method, returns string
        Register(
            "System.Object", "ToString",
            (nint)(delegate*<object, string>)&ObjectHelpers.ToString,
            0, ReturnKind.IntPtr, true, true);
    }

    /// <summary>
    /// Register Int32 methods with their wrapper addresses.
    /// </summary>
    private static void RegisterInt32Methods()
    {
        // Int32.ToString() - instance method on value type
        // For value types, 'this' is a pointer to the value
        Register(
            "System.Int32", "ToString",
            (nint)(delegate*<int*, string>)&Int32Helpers.ToString,
            0, ReturnKind.IntPtr, true, false);
    }

    /// <summary>
    /// Register an AOT method.
    /// </summary>
    private static void Register(string typeName, string methodName, nint nativeCode,
                                  byte argCount, ReturnKind returnKind, bool hasThis, bool isVirtual)
    {
        if (_entries == null || _count >= MaxEntries)
            return;

        uint typeHash = HashString(typeName);
        uint methodHash = HashString(methodName);

        _entries[_count].TypeNameHash = typeHash;
        _entries[_count].MethodNameHash = methodHash;
        _entries[_count].NativeCode = nativeCode;
        _entries[_count].ArgCount = argCount;
        _entries[_count].ReturnKind = returnKind;
        _entries[_count].HasThis = hasThis;
        _entries[_count].IsVirtual = isVirtual;
        _count++;
    }

    /// <summary>
    /// Look up an AOT method by type and method name.
    /// Returns true if found and populates the entry.
    /// </summary>
    public static bool TryLookup(byte* typeName, byte* methodName, byte argCount, out AotMethodEntry entry)
    {
        entry = default;

        if (_entries == null || typeName == null || methodName == null)
            return false;

        uint typeHash = HashBytes(typeName);
        uint methodHash = HashBytes(methodName);

        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].TypeNameHash == typeHash &&
                _entries[i].MethodNameHash == methodHash)
            {
                // For overloaded methods, match by arg count
                // Note: argCount includes 'this' for instance methods
                if (_entries[i].ArgCount == argCount ||
                    (argCount == 0 && !_entries[i].HasThis))  // Don't enforce count if not provided
                {
                    entry = _entries[i];
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a type name is a well-known AOT type.
    /// </summary>
    public static bool IsWellKnownAotType(byte* typeName)
    {
        if (typeName == null)
            return false;

        // Check for System.String
        if (StringMatches(typeName, "System.String"))
            return true;

        // Check for System.Object
        if (StringMatches(typeName, "System.Object"))
            return true;

        // Check for System.Int32
        if (StringMatches(typeName, "System.Int32"))
            return true;

        return false;
    }

    /// <summary>
    /// Hash a managed string for lookup.
    /// </summary>
    private static uint HashString(string s)
    {
        if (s == null)
            return 0;

        uint hash = 5381;
        for (int i = 0; i < s.Length; i++)
        {
            hash = ((hash << 5) + hash) ^ (uint)s[i];
        }
        return hash;
    }

    /// <summary>
    /// Hash a null-terminated byte string.
    /// </summary>
    private static uint HashBytes(byte* s)
    {
        if (s == null)
            return 0;

        uint hash = 5381;
        while (*s != 0)
        {
            hash = ((hash << 5) + hash) ^ *s;
            s++;
        }
        return hash;
    }

    /// <summary>
    /// Check if a byte string matches a managed string.
    /// </summary>
    private static bool StringMatches(byte* bytes, string str)
    {
        if (bytes == null || str == null)
            return false;

        for (int i = 0; i < str.Length; i++)
        {
            if (bytes[i] == 0 || bytes[i] != (byte)str[i])
                return false;
        }
        return bytes[str.Length] == 0;
    }
}

/// <summary>
/// Wrapper methods for String operations.
/// These are thin wrappers that forward to the actual String methods.
/// The JIT calls these wrappers because we can get their function pointers.
/// </summary>
public static unsafe class StringHelpers
{
    /// <summary>
    /// Wrapper for String.get_Length.
    /// </summary>
    public static int GetLength(string s)
    {
        if (s == null)
            return 0;
        return s.Length;
    }

    /// <summary>
    /// Wrapper for String.Concat(string, string).
    /// </summary>
    public static string Concat2(string? str0, string? str1)
    {
        return string.Concat(str0, str1);
    }

    /// <summary>
    /// Wrapper for String.Concat(string, string, string).
    /// </summary>
    public static string Concat3(string? str0, string? str1, string? str2)
    {
        return string.Concat(str0, str1, str2);
    }

    /// <summary>
    /// Wrapper for String indexer (get_Chars).
    /// </summary>
    public static char GetChars(string s, int index)
    {
        if (s == null)
            return '\0';
        if ((uint)index >= (uint)s.Length)
            return '\0';  // Could throw, but simpler for now
        return s[index];
    }

    /// <summary>
    /// Wrapper for String.IsNullOrEmpty.
    /// </summary>
    public static bool IsNullOrEmpty(string? value)
    {
        return string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// Wrapper for String.Equals(string).
    /// </summary>
    public static bool Equals(string s, string? other)
    {
        if (s == null)
            return other == null;
        return s.Equals(other);
    }
}

/// <summary>
/// Wrapper methods for Object operations.
/// These are thin wrappers that provide base Object behavior.
/// NOTE: We avoid virtual calls to prevent triggering unboxing code generation
/// which requires System.Runtime.RuntimeExports.
/// </summary>
public static unsafe class ObjectHelpers
{
    /// <summary>
    /// Wrapper for Object..ctor().
    /// Object's constructor does nothing - the object is already allocated.
    /// </summary>
    public static void Ctor(object obj)
    {
        // Nothing to do - object is already allocated
    }

    /// <summary>
    /// Wrapper for Object.GetHashCode().
    /// Returns a pointer-based hash code (base Object behavior).
    /// </summary>
    public static int GetHashCode(object obj)
    {
        if (obj == null)
            return 0;
        // Use pointer-based hash (this is what base Object.GetHashCode() does)
        return (int)(nint)System.Runtime.CompilerServices.Unsafe.AsPointer(ref obj);
    }

    /// <summary>
    /// Wrapper for Object.Equals(object).
    /// Uses reference equality (base Object behavior).
    /// </summary>
    public static bool Equals(object obj, object? other)
    {
        // Base Object.Equals uses reference equality
        return ReferenceEquals(obj, other);
    }

    /// <summary>
    /// Wrapper for Object.ToString().
    /// Returns a type name placeholder (avoids virtual dispatch).
    /// </summary>
    public static string ToString(object obj)
    {
        if (obj == null)
            return "null";
        // Return a simple placeholder - avoids virtual ToString() dispatch
        return "object";
    }

    private static bool ReferenceEquals(object? a, object? b)
    {
        return (object?)a == (object?)b;
    }
}

/// <summary>
/// Wrapper methods for Int32 operations.
/// For value types, the 'this' pointer is a pointer to the value.
/// </summary>
public static unsafe class Int32Helpers
{
    /// <summary>
    /// Wrapper for Int32.ToString().
    /// Takes pointer to int value and returns string representation.
    /// </summary>
    public static string ToString(int* value)
    {
        if (value == null)
            return "0";
        return System.Int32.FormatInt32(*value);
    }
}
