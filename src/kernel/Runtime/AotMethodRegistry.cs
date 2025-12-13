// ProtonOS kernel - AOT Method Registry
// Provides lookup for AOT-compiled korlib methods that can be called from JIT code.
// These methods have no JIT metadata - they're compiled directly into the kernel.

using System;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;
using ProtonOS.Runtime.Reflection;

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

        // Register well-known Type methods (for reflection)
        RegisterTypeMethods();

        // Register well-known Int32 methods
        RegisterInt32Methods();

        // Register well-known Exception methods
        RegisterExceptionMethods();

        // Register Delegate methods
        RegisterDelegateMethods();

        _initialized = true;
        DebugConsole.WriteLine(string.Format("[AotRegistry] Initialized with {0} methods", _count));
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

        // String.op_Equality (static) - 2 string parameters
        Register(
            "System.String", "op_Equality",
            (nint)(delegate*<string?, string?, bool>)&StringHelpers.OpEquality,
            2, ReturnKind.Int32, false, false);

        // String.op_Inequality (static) - 2 string parameters
        Register(
            "System.String", "op_Inequality",
            (nint)(delegate*<string?, string?, bool>)&StringHelpers.OpInequality,
            2, ReturnKind.Int32, false, false);

        // String.GetPinnableReference() - 0 parameters, HasThis=true, returns ref char (pointer)
        Register(
            "System.String", "GetPinnableReference",
            (nint)(delegate*<string, nint>)&StringHelpers.GetPinnableReference,
            0, ReturnKind.IntPtr, true, false);

        // String.CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        // 4 parameters (sourceIndex, destination, destinationIndex, count), HasThis=true, returns void
        Register(
            "System.String", "CopyTo",
            (nint)(delegate*<string, int, char[], int, int, void>)&StringHelpers.CopyTo,
            4, ReturnKind.Void, true, false);

        // String constructor: .ctor(char[], int, int) - 3 parameters, static (factory method)
        // This is used by StringBuilder.ToString to create strings from char arrays.
        // The JIT transforms newobj String::.ctor to a call to this factory method.
        Register(
            "System.String", ".ctor",
            (nint)(delegate*<char[], int, int, string>)&StringHelpers.Ctor_CharArrayStartLength,
            3, ReturnKind.IntPtr, false, false);

        // String constructor: .ctor(char[]) - 1 parameter, static (factory method)
        Register(
            "System.String", ".ctor",
            (nint)(delegate*<char[], string>)&StringHelpers.Ctor_CharArray,
            1, ReturnKind.IntPtr, false, false);

        // String.Format overloads - static methods for formatted strings
        // Format(string, object) - 2 parameters
        Register(
            "System.String", "Format",
            (nint)(delegate*<string, object?, string>)&StringHelpers.Format1,
            2, ReturnKind.IntPtr, false, false);

        // Format(string, object, object) - 3 parameters
        Register(
            "System.String", "Format",
            (nint)(delegate*<string, object?, object?, string>)&StringHelpers.Format2,
            3, ReturnKind.IntPtr, false, false);

        // Format(string, object, object, object) - 4 parameters
        Register(
            "System.String", "Format",
            (nint)(delegate*<string, object?, object?, object?, string>)&StringHelpers.Format3,
            4, ReturnKind.IntPtr, false, false);
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

        // Object.GetType() - instance method, returns Type (non-virtual, final)
        Register(
            "System.Object", "GetType",
            (nint)(delegate*<object, Type>)&ObjectHelpers.GetType,
            0, ReturnKind.IntPtr, true, false);  // Not virtual - GetType is final
    }

    /// <summary>
    /// Register Type methods (for reflection support).
    /// These bypass virtual dispatch since RuntimeType's vtable isn't properly set up by AOT.
    /// </summary>
    private static void RegisterTypeMethods()
    {
        // Type.get_Name - property getter, returns string
        Register(
            "System.Type", "get_Name",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetName,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // MemberInfo.get_Name - In .NET, Type inherits from MemberInfo
        // The compiler generates calls to MemberInfo.get_Name when calling t.Name on a Type
        Register(
            "System.Reflection.MemberInfo", "get_Name",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetName,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // Type.get_FullName - property getter, returns string
        Register(
            "System.Type", "get_FullName",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetFullName,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // Type.get_Namespace - property getter, returns string
        Register(
            "System.Type", "get_Namespace",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetNamespace,
            0, ReturnKind.IntPtr, true, true);  // Virtual
    }

    /// <summary>
    /// Register Int32 methods with their wrapper addresses.
    /// </summary>
    private static void RegisterInt32Methods()
    {
        // Int32.ToString() - instance method on boxed value type
        // For boxed value types, 'this' is a pointer to the boxed object
        Register(
            "System.Int32", "ToString",
            (nint)(delegate*<nint, string>)&Int32Helpers.ToString,
            0, ReturnKind.IntPtr, true, false);

        // Int32.GetHashCode() - returns the int value itself
        Register(
            "System.Int32", "GetHashCode",
            (nint)(delegate*<nint, int>)&Int32Helpers.GetHashCode,
            0, ReturnKind.Int32, true, true);  // virtual method
    }

    /// <summary>
    /// Register Exception constructor methods for JIT code.
    /// These allow JIT-compiled code to call newobj for exception types.
    /// </summary>
    private static void RegisterExceptionMethods()
    {
        // Exception constructors - factory style (JIT transforms newobj to call)
        // Exception() - 0 parameters
        Register(
            "System.Exception", ".ctor",
            (nint)(delegate*<Exception>)&ExceptionHelpers.Ctor_Exception,
            0, ReturnKind.IntPtr, false, false);

        // Exception(string) - 1 parameter
        Register(
            "System.Exception", ".ctor",
            (nint)(delegate*<string?, Exception>)&ExceptionHelpers.Ctor_Exception_String,
            1, ReturnKind.IntPtr, false, false);

        // ArgumentException constructors
        Register(
            "System.ArgumentException", ".ctor",
            (nint)(delegate*<ArgumentException>)&ExceptionHelpers.Ctor_ArgumentException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.ArgumentException", ".ctor",
            (nint)(delegate*<string?, ArgumentException>)&ExceptionHelpers.Ctor_ArgumentException_String,
            1, ReturnKind.IntPtr, false, false);

        // ArgumentNullException constructors
        Register(
            "System.ArgumentNullException", ".ctor",
            (nint)(delegate*<ArgumentNullException>)&ExceptionHelpers.Ctor_ArgumentNullException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.ArgumentNullException", ".ctor",
            (nint)(delegate*<string?, ArgumentNullException>)&ExceptionHelpers.Ctor_ArgumentNullException_String,
            1, ReturnKind.IntPtr, false, false);

        // InvalidOperationException constructors
        Register(
            "System.InvalidOperationException", ".ctor",
            (nint)(delegate*<InvalidOperationException>)&ExceptionHelpers.Ctor_InvalidOperationException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.InvalidOperationException", ".ctor",
            (nint)(delegate*<string?, InvalidOperationException>)&ExceptionHelpers.Ctor_InvalidOperationException_String,
            1, ReturnKind.IntPtr, false, false);

        // NotSupportedException constructors
        Register(
            "System.NotSupportedException", ".ctor",
            (nint)(delegate*<NotSupportedException>)&ExceptionHelpers.Ctor_NotSupportedException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.NotSupportedException", ".ctor",
            (nint)(delegate*<string?, NotSupportedException>)&ExceptionHelpers.Ctor_NotSupportedException_String,
            1, ReturnKind.IntPtr, false, false);

        // NotImplementedException constructors
        Register(
            "System.NotImplementedException", ".ctor",
            (nint)(delegate*<NotImplementedException>)&ExceptionHelpers.Ctor_NotImplementedException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.NotImplementedException", ".ctor",
            (nint)(delegate*<string?, NotImplementedException>)&ExceptionHelpers.Ctor_NotImplementedException_String,
            1, ReturnKind.IntPtr, false, false);

        // IndexOutOfRangeException constructors
        Register(
            "System.IndexOutOfRangeException", ".ctor",
            (nint)(delegate*<IndexOutOfRangeException>)&ExceptionHelpers.Ctor_IndexOutOfRangeException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.IndexOutOfRangeException", ".ctor",
            (nint)(delegate*<string?, IndexOutOfRangeException>)&ExceptionHelpers.Ctor_IndexOutOfRangeException_String,
            1, ReturnKind.IntPtr, false, false);

        // NullReferenceException constructors
        Register(
            "System.NullReferenceException", ".ctor",
            (nint)(delegate*<NullReferenceException>)&ExceptionHelpers.Ctor_NullReferenceException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.NullReferenceException", ".ctor",
            (nint)(delegate*<string?, NullReferenceException>)&ExceptionHelpers.Ctor_NullReferenceException_String,
            1, ReturnKind.IntPtr, false, false);

        // InvalidCastException constructors
        Register(
            "System.InvalidCastException", ".ctor",
            (nint)(delegate*<InvalidCastException>)&ExceptionHelpers.Ctor_InvalidCastException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.InvalidCastException", ".ctor",
            (nint)(delegate*<string?, InvalidCastException>)&ExceptionHelpers.Ctor_InvalidCastException_String,
            1, ReturnKind.IntPtr, false, false);

        // FormatException constructors
        Register(
            "System.FormatException", ".ctor",
            (nint)(delegate*<FormatException>)&ExceptionHelpers.Ctor_FormatException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.FormatException", ".ctor",
            (nint)(delegate*<string?, FormatException>)&ExceptionHelpers.Ctor_FormatException_String,
            1, ReturnKind.IntPtr, false, false);
    }

    /// <summary>
    /// Register Delegate methods for multicast delegate support.
    /// </summary>
    private static void RegisterDelegateMethods()
    {
        // Delegate.Combine(Delegate?, Delegate?) - static method, returns Delegate?
        Register(
            "System.Delegate", "Combine",
            (nint)(delegate*<Delegate?, Delegate?, Delegate?>)&DelegateHelpers.Combine,
            2, ReturnKind.IntPtr, false, false);

        // Delegate.Remove(Delegate?, Delegate?) - static method, returns Delegate?
        Register(
            "System.Delegate", "Remove",
            (nint)(delegate*<Delegate?, Delegate?, Delegate?>)&DelegateHelpers.Remove,
            2, ReturnKind.IntPtr, false, false);

        // MulticastDelegate.CombineImpl(Delegate?) - instance virtual method for vtable slot 3
        // This is called through the vtable when combining multicast delegates
        Register(
            "System.MulticastDelegate", "CombineImpl",
            (nint)(delegate*<MulticastDelegate, Delegate?, Delegate?>)&DelegateHelpers.CombineImplWrapper,
            1, ReturnKind.IntPtr, true, true);

        // MulticastDelegate.RemoveImpl(Delegate) - instance virtual method for vtable slot 4
        // This is called through the vtable when removing from multicast delegates
        Register(
            "System.MulticastDelegate", "RemoveImpl",
            (nint)(delegate*<MulticastDelegate, Delegate, Delegate?>)&DelegateHelpers.RemoveImplWrapper,
            1, ReturnKind.IntPtr, true, true);
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
    /// Look up an AOT method by type and method name (managed strings).
    /// Returns the native code address or 0 if not found.
    /// </summary>
    public static nint LookupByName(string typeName, string methodName)
    {
        if (_entries == null || typeName == null || methodName == null)
            return 0;

        uint typeHash = HashString(typeName);
        uint methodHash = HashString(methodName);

        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].TypeNameHash == typeHash &&
                _entries[i].MethodNameHash == methodHash)
            {
                return _entries[i].NativeCode;
            }
        }

        return 0;
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

        // Exception types
        if (StringMatches(typeName, "System.Exception"))
            return true;
        if (StringMatches(typeName, "System.ArgumentException"))
            return true;
        if (StringMatches(typeName, "System.ArgumentNullException"))
            return true;
        if (StringMatches(typeName, "System.ArgumentOutOfRangeException"))
            return true;
        if (StringMatches(typeName, "System.InvalidOperationException"))
            return true;
        if (StringMatches(typeName, "System.NotSupportedException"))
            return true;
        if (StringMatches(typeName, "System.NotImplementedException"))
            return true;
        if (StringMatches(typeName, "System.IndexOutOfRangeException"))
            return true;
        if (StringMatches(typeName, "System.NullReferenceException"))
            return true;
        if (StringMatches(typeName, "System.InvalidCastException"))
            return true;
        if (StringMatches(typeName, "System.FormatException"))
            return true;

        // Delegate types
        if (StringMatches(typeName, "System.Delegate"))
            return true;
        if (StringMatches(typeName, "System.MulticastDelegate"))
            return true;

        // Reflection types
        if (StringMatches(typeName, "System.Type"))
            return true;
        if (StringMatches(typeName, "System.RuntimeType"))
            return true;
        if (StringMatches(typeName, "System.Reflection.MemberInfo"))
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

    /// <summary>
    /// Wrapper for String.op_Equality (== operator).
    /// </summary>
    public static bool OpEquality(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Wrapper for String.op_Inequality (!= operator).
    /// </summary>
    public static bool OpInequality(string? a, string? b)
    {
        return !OpEquality(a, b);
    }

    /// <summary>
    /// Wrapper for String.GetPinnableReference().
    /// Returns a pointer to the first character of the string.
    /// </summary>
    public static unsafe nint GetPinnableReference(string s)
    {
        if (s == null || s.Length == 0)
            return 0;
        // Get reference and convert to pointer
        fixed (char* ptr = &s.GetPinnableReference())
        {
            return (nint)ptr;
        }
    }

    /// <summary>
    /// Wrapper for String.CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count).
    /// Copies characters from the string to a char array.
    /// </summary>
    public static void CopyTo(string s, int sourceIndex, char[] destination, int destinationIndex, int count)
    {
        if (s == null || destination == null)
            return;
        if (sourceIndex < 0 || destinationIndex < 0 || count <= 0)
            return;
        if (sourceIndex + count > s.Length)
            return;
        if (destinationIndex + count > destination.Length)
            return;

        for (int i = 0; i < count; i++)
        {
            destination[destinationIndex + i] = s[sourceIndex + i];
        }
    }

    /// <summary>
    /// Factory method for String..ctor(char[], int, int).
    /// Creates a new string from a portion of a char array.
    /// </summary>
    public static string Ctor_CharArrayStartLength(char[] value, int startIndex, int length)
    {
        return string.Ctor_CharArrayStartLength(value, startIndex, length);
    }

    /// <summary>
    /// Factory method for String..ctor(char[]).
    /// Creates a new string from a char array.
    /// </summary>
    public static string Ctor_CharArray(char[] value)
    {
        return string.Ctor_CharArray(value);
    }

    /// <summary>
    /// Wrapper for String.Format(string, object).
    /// </summary>
    public static string Format1(string format, object? arg0)
    {
        return string.Format(format, arg0);
    }

    /// <summary>
    /// Wrapper for String.Format(string, object, object).
    /// </summary>
    public static string Format2(string format, object? arg0, object? arg1)
    {
        return string.Format(format, arg0, arg1);
    }

    /// <summary>
    /// Wrapper for String.Format(string, object, object, object).
    /// </summary>
    public static string Format3(string format, object? arg0, object? arg1, object? arg2)
    {
        return string.Format(format, arg0, arg1, arg2);
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

    /// <summary>
    /// Wrapper for Object.GetType().
    /// Returns the Type object for this object's runtime type.
    /// </summary>
    public static Type GetType(object obj)
    {
        if (obj == null)
            return null!;

        // Get the MethodTable pointer from the object (first field)
        // Object layout: [MethodTable* m_pMethodTable, ...fields...]
        // First dereference: get the object pointer from the reference
        void* objPtr = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref obj);

        // Second dereference: read the MethodTable from the start of the object
        void* mt = *(void**)objPtr;

        // Create and return a RuntimeType wrapping the MethodTable
        // Cast to System.Runtime.MethodTable* which RuntimeType expects
        return new RuntimeType((System.Runtime.MethodTable*)mt);
    }

    private static bool ReferenceEquals(object? a, object? b)
    {
        return (object?)a == (object?)b;
    }
}

/// <summary>
/// Wrapper methods for Int32 operations.
/// When called via vtable dispatch on a boxed value type, 'this' is the boxed object pointer.
/// The actual value is at offset 8 (after the MethodTable pointer).
/// </summary>
public static unsafe class Int32Helpers
{
    /// <summary>
    /// Wrapper for Int32.ToString() when called on a boxed Int32.
    /// When called through vtable dispatch, 'this' is the boxed object pointer.
    /// The actual int value is at offset 8 (after the MethodTable pointer).
    /// </summary>
    public static string ToString(nint thisPtr)
    {
        if (thisPtr == 0)
            return "0";
        // thisPtr is a boxed object: [MethodTable*][int value]
        // Value is at offset 8
        int* valuePtr = (int*)(thisPtr + 8);
        return System.Int32.FormatInt32(*valuePtr);
    }

    /// <summary>
    /// Wrapper for Int32.GetHashCode() when called on a boxed Int32.
    /// Returns the int value itself as the hash code.
    /// </summary>
    public static int GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0)
            return 0;
        // thisPtr is a boxed object: [MethodTable*][int value]
        // Value is at offset 8
        int* valuePtr = (int*)(thisPtr + 8);
        return *valuePtr;
    }
}

/// <summary>
/// Helper methods for Type operations.
/// These provide implementations for Type virtual methods that the JIT can call directly,
/// bypassing the broken vtable dispatch for RuntimeType.
/// </summary>
public static unsafe class TypeMethodHelpers
{
    /// <summary>
    /// Get the Name of a Type.
    /// </summary>
    public static string? GetName(Type type)
    {
        if (type == null)
            return null;

        // Get the RuntimeType's internal MethodTable pointer
        // Type object layout: [MethodTable*][_pMethodTable field at offset 8]
        void* typePtr = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref type);
        if (typePtr == null)
            return null;

        // Read the _pMethodTable field (first field after MT pointer, at offset 8)
        void* storedMT = *(void**)((byte*)typePtr + 8);
        if (storedMT == null)
            return "RuntimeType";

        // Look up the type info from the reflection runtime
        uint asmId = 0, token = 0;
        ReflectionRuntime.GetTypeInfo(storedMT, &asmId, &token);

        if (token == 0)
            return "RuntimeType";

        // Get the type name from metadata
        byte* namePtr = ReflectionRuntime.GetTypeName(asmId, token);
        if (namePtr == null)
            return "RuntimeType";

        return BytePtrToString(namePtr);
    }

    /// <summary>
    /// Get the FullName of a Type (Namespace.Name).
    /// </summary>
    public static string? GetFullName(Type type)
    {
        if (type == null)
            return null;

        string? ns = GetNamespace(type);
        string? name = GetName(type);

        if (string.IsNullOrEmpty(ns))
            return name;

        return ns + "." + name;
    }

    /// <summary>
    /// Get the Namespace of a Type.
    /// </summary>
    public static string? GetNamespace(Type type)
    {
        if (type == null)
            return null;

        // Get the RuntimeType's internal MethodTable pointer
        void* typePtr = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref type);
        if (typePtr == null)
            return null;

        // Read the _pMethodTable field (first field after MT pointer, at offset 8)
        void* storedMT = *(void**)((byte*)typePtr + 8);
        if (storedMT == null)
            return null;

        // Look up the type info from the reflection runtime
        uint asmId = 0, token = 0;
        ReflectionRuntime.GetTypeInfo(storedMT, &asmId, &token);

        if (token == 0)
            return null;

        // Get the namespace from metadata
        byte* nsPtr = ReflectionRuntime.GetTypeNamespace(asmId, token);
        if (nsPtr == null || *nsPtr == 0)
            return null;

        return BytePtrToString(nsPtr);
    }

    /// <summary>
    /// Convert a null-terminated UTF-8 byte pointer to a string.
    /// </summary>
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
/// Factory methods for Exception types.
/// These are used by JIT code to create exception instances via newobj.
/// The JIT transforms newobj Exception::.ctor to a call to these factory methods.
/// </summary>
public static class ExceptionHelpers
{
    // Exception
    public static Exception Ctor_Exception() => new Exception();
    public static Exception Ctor_Exception_String(string? message) => new Exception(message);

    // ArgumentException
    public static ArgumentException Ctor_ArgumentException() => new ArgumentException();
    public static ArgumentException Ctor_ArgumentException_String(string? message) => new ArgumentException(message);

    // ArgumentNullException
    public static ArgumentNullException Ctor_ArgumentNullException() => new ArgumentNullException();
    public static ArgumentNullException Ctor_ArgumentNullException_String(string? paramName) => new ArgumentNullException(paramName);

    // InvalidOperationException
    public static InvalidOperationException Ctor_InvalidOperationException() => new InvalidOperationException();
    public static InvalidOperationException Ctor_InvalidOperationException_String(string? message) => new InvalidOperationException(message);

    // NotSupportedException
    public static NotSupportedException Ctor_NotSupportedException() => new NotSupportedException();
    public static NotSupportedException Ctor_NotSupportedException_String(string? message) => new NotSupportedException(message);

    // NotImplementedException
    public static NotImplementedException Ctor_NotImplementedException() => new NotImplementedException();
    public static NotImplementedException Ctor_NotImplementedException_String(string? message) => new NotImplementedException(message);

    // IndexOutOfRangeException
    public static IndexOutOfRangeException Ctor_IndexOutOfRangeException() => new IndexOutOfRangeException();
    public static IndexOutOfRangeException Ctor_IndexOutOfRangeException_String(string? message) => new IndexOutOfRangeException(message);

    // NullReferenceException
    public static NullReferenceException Ctor_NullReferenceException() => new NullReferenceException();
    public static NullReferenceException Ctor_NullReferenceException_String(string? message) => new NullReferenceException(message);

    // InvalidCastException
    public static InvalidCastException Ctor_InvalidCastException() => new InvalidCastException();
    public static InvalidCastException Ctor_InvalidCastException_String(string? message) => new InvalidCastException(message);

    // FormatException
    public static FormatException Ctor_FormatException() => new FormatException();
    public static FormatException Ctor_FormatException_String(string? message) => new FormatException(message);
}

/// <summary>
/// Wrapper methods for Delegate operations.
/// These forward to the actual Delegate.Combine/Remove methods in korlib.
/// </summary>
public static class DelegateHelpers
{
    /// <summary>
    /// Wrapper for Delegate.Combine(Delegate?, Delegate?).
    /// Combines two delegates into a multicast delegate.
    /// </summary>
    public static Delegate? Combine(Delegate? a, Delegate? b)
    {
        return Delegate.Combine(a, b);
    }

    /// <summary>
    /// Wrapper for Delegate.Remove(Delegate?, Delegate?).
    /// Removes a delegate from a multicast delegate.
    /// </summary>
    public static Delegate? Remove(Delegate? source, Delegate? value)
    {
        return Delegate.Remove(source, value);
    }

    /// <summary>
    /// Wrapper for MulticastDelegate.CombineImpl for vtable slot population.
    /// This is called through the vtable when combining delegates.
    /// </summary>
    public static Delegate? CombineImplWrapper(MulticastDelegate self, Delegate? d)
    {
        return self.InvokeCombineImpl(d);
    }

    /// <summary>
    /// Wrapper for MulticastDelegate.RemoveImpl for vtable slot population.
    /// This is called through the vtable when removing delegates.
    /// </summary>
    public static Delegate? RemoveImplWrapper(MulticastDelegate self, Delegate d)
    {
        return self.InvokeRemoveImpl(d);
    }
}
