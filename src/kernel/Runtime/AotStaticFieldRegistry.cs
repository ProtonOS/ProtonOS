// ProtonOS kernel - AOT Static Field Registry
// Provides lookup for static fields in AOT-compiled types that are excluded from korlib.dll.
// When JIT code accesses static fields on types like Boolean, IntPtr, etc., this registry
// provides the actual addresses of those fields in the AOT kernel image.

using System;

namespace ProtonOS.Runtime;

/// <summary>
/// Entry for a registered AOT static field.
/// </summary>
public unsafe struct AotStaticFieldEntry
{
    /// <summary>Type name hash (e.g., hash of "System.Boolean").</summary>
    public ulong TypeNameHash;

    /// <summary>Field name hash (e.g., hash of "TrueString").</summary>
    public ulong FieldNameHash;

    /// <summary>Address of the static field in AOT memory.</summary>
    public nint Address;

    /// <summary>Size of the field in bytes.</summary>
    public int Size;

    /// <summary>True if the field is signed (for integer types).</summary>
    public bool IsSigned;

    /// <summary>True if entry is valid (not empty).</summary>
    public bool IsValid;
}

/// <summary>
/// Registry for static fields in AOT-compiled types excluded from korlib.dll.
/// This allows JIT code to access static fields like Boolean.TrueString, IntPtr.Zero, etc.
/// without needing the metadata from korlib.dll.
/// </summary>
public static unsafe class AotStaticFieldRegistry
{
    // Hash table for field lookup
    private const int CacheSize = 128;
    private const int CacheMask = CacheSize - 1;

    // Fixed-size array of entries (hash table with linear probing)
    private static AotStaticFieldEntry* _entries;
    private static int _count;
    private static bool _initialized;

    /// <summary>
    /// Initialize the registry. Must be called before any other operations.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        // Allocate hash table
        int size = CacheSize * sizeof(AotStaticFieldEntry);
        _entries = (AotStaticFieldEntry*)Memory.HeapAllocator.AllocZeroed((ulong)size);
        if (_entries == null)
        {
            Platform.DebugConsole.WriteLine("[AotStaticFieldRegistry] Failed to allocate cache");
            return;
        }

        // Zero out
        byte* ptr = (byte*)_entries;
        for (int i = 0; i < size; i++)
            ptr[i] = 0;

        _count = 0;
        _initialized = true;
    }

    /// <summary>
    /// Register an AOT static field.
    /// </summary>
    /// <param name="typeName">Full type name (e.g., "System.Boolean")</param>
    /// <param name="fieldName">Field name (e.g., "TrueString")</param>
    /// <param name="address">Address of the static field in AOT memory</param>
    /// <param name="size">Size of the field in bytes</param>
    /// <param name="isSigned">True if the field is a signed integer type</param>
    public static void Register(string typeName, string fieldName, nint address, int size, bool isSigned = false)
    {
        if (!_initialized || _entries == null) return;
        if (_count >= CacheSize - 1) return; // Leave room for probing

        ulong typeHash = HashString(typeName);
        ulong fieldHash = HashString(fieldName);
        ulong combinedHash = typeHash ^ (fieldHash << 13);
        int index = (int)(combinedHash & CacheMask);

        // Linear probing to find empty slot
        int probes = 0;
        while (_entries[index].IsValid && probes < CacheSize)
        {
            index = (index + 1) & CacheMask;
            probes++;
        }

        if (probes >= CacheSize) return; // Table full

        _entries[index] = new AotStaticFieldEntry
        {
            TypeNameHash = typeHash,
            FieldNameHash = fieldHash,
            Address = address,
            Size = size,
            IsSigned = isSigned,
            IsValid = true
        };
        _count++;
    }

    /// <summary>
    /// Look up an AOT static field by type and field name.
    /// </summary>
    /// <returns>True if found, with entry populated</returns>
    public static bool Lookup(string typeName, string fieldName, out AotStaticFieldEntry entry)
    {
        entry = default;
        if (!_initialized || _entries == null) return false;

        ulong typeHash = HashString(typeName);
        ulong fieldHash = HashString(fieldName);
        ulong combinedHash = typeHash ^ (fieldHash << 13);
        int index = (int)(combinedHash & CacheMask);

        // Linear probing to find entry
        int probes = 0;
        while (_entries[index].IsValid && probes < CacheSize)
        {
            if (_entries[index].TypeNameHash == typeHash && _entries[index].FieldNameHash == fieldHash)
            {
                entry = _entries[index];
                return true;
            }
            index = (index + 1) & CacheMask;
            probes++;
        }

        return false;
    }

    /// <summary>
    /// Look up an AOT static field by type and field name hashes.
    /// Faster version when hashes are already computed.
    /// </summary>
    public static bool LookupByHash(ulong typeHash, ulong fieldHash, out AotStaticFieldEntry entry)
    {
        entry = default;
        if (!_initialized || _entries == null) return false;

        ulong combinedHash = typeHash ^ (fieldHash << 13);
        int index = (int)(combinedHash & CacheMask);

        // Linear probing to find entry
        int probes = 0;
        while (_entries[index].IsValid && probes < CacheSize)
        {
            if (_entries[index].TypeNameHash == typeHash && _entries[index].FieldNameHash == fieldHash)
            {
                entry = _entries[index];
                return true;
            }
            index = (index + 1) & CacheMask;
            probes++;
        }

        return false;
    }

    /// <summary>
    /// Get the address of an AOT static field directly.
    /// Used by JIT inlining for primitive type methods like Boolean.ToString().
    /// </summary>
    /// <returns>The field address, or 0 if not found</returns>
    public static nint TryGetFieldAddress(string typeName, string fieldName)
    {
        if (Lookup(typeName, fieldName, out AotStaticFieldEntry entry))
            return entry.Address;
        return 0;
    }

    /// <summary>
    /// FNV-1a hash for strings.
    /// </summary>
    private static ulong HashString(string s)
    {
        if (s == null) return 0;
        ulong hash = 14695981039346656037UL;
        for (int i = 0; i < s.Length; i++)
        {
            hash ^= s[i];
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>
    /// Get the count of registered fields.
    /// </summary>
    public static int Count => _count;

    /// <summary>
    /// Check if the registry is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Register all known AOT static fields from excluded types.
    /// Call this after Initialize() and before JIT compilation starts.
    /// </summary>
    public static void RegisterKnownFields()
    {
        // Boolean.TrueString and Boolean.FalseString
        // These are static readonly string fields - we need to get their addresses
        // In C#, static readonly fields are stored in the AOT data segment
        RegisterBooleanFields();

        // IntPtr.Zero and UIntPtr.Zero
        // These are static readonly fields
        RegisterIntPtrFields();

        // String.Empty
        // Note: In korlib, Empty is implemented as a property, but the C# compiler
        // generates ldsfld for string.Empty, expecting a field.
        RegisterStringFields();

        Platform.DebugConsole.Write("[AotStaticFieldRegistry] Registered ");
        Platform.DebugConsole.WriteDecimal((uint)_count);
        Platform.DebugConsole.WriteLine(" AOT static fields");
    }

    /// <summary>
    /// Register Boolean static fields.
    /// </summary>
    private static void RegisterBooleanFields()
    {
        // Boolean.TrueString and Boolean.FalseString are static readonly string fields.
        // We create storage for these and allocate the string values ourselves.
        // This avoids issues with AOT field address lookup while providing the same values.

        // Allocate storage for Boolean's static fields (2 string pointers = 16 bytes)
        nint* boolStaticStorage = (nint*)Memory.HeapAllocator.AllocZeroed(16);
        if (boolStaticStorage == null) return;

        // Create the string instances ("True" and "False")
        // Note: These will be separate instances from the AOT strings, but that's fine
        // since string comparison uses value equality, not reference equality.
        // We use individual characters to avoid AOT string literal issues.
        char* trueChars = stackalloc char[4];
        trueChars[0] = 'T';
        trueChars[1] = 'r';
        trueChars[2] = 'u';
        trueChars[3] = 'e';
        string trueStr = new string(trueChars, 0, 4);

        char* falseChars = stackalloc char[5];
        falseChars[0] = 'F';
        falseChars[1] = 'a';
        falseChars[2] = 'l';
        falseChars[3] = 's';
        falseChars[4] = 'e';
        string falseStr = new string(falseChars, 0, 5);

        // Store the object references (pointers to the string objects)
        boolStaticStorage[0] = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref trueStr);
        boolStaticStorage[1] = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref falseStr);

        Register("System.Boolean", "TrueString", (nint)(&boolStaticStorage[0]), 8, false);
        Register("System.Boolean", "FalseString", (nint)(&boolStaticStorage[1]), 8, false);
    }

    /// <summary>
    /// Register IntPtr and UIntPtr static fields.
    /// </summary>
    private static void RegisterIntPtrFields()
    {
        // IntPtr.Zero and UIntPtr.Zero are static readonly fields containing 0
        // These are value types, so we allocate storage and copy the values

        // Allocate storage for IntPtr.Zero and UIntPtr.Zero (2 x 8 bytes = 16 bytes)
        nint* intPtrStaticStorage = (nint*)Memory.HeapAllocator.AllocZeroed(16);
        if (intPtrStaticStorage == null) return;

        // The values are just 0, but we need a fixed address for the storage
        intPtrStaticStorage[0] = nint.Zero;
        intPtrStaticStorage[1] = (nint)nuint.Zero;

        Register("System.IntPtr", "Zero", (nint)(&intPtrStaticStorage[0]), 8, true);
        Register("System.UIntPtr", "Zero", (nint)(&intPtrStaticStorage[1]), 8, false);
    }

    /// <summary>
    /// Register String static fields.
    /// </summary>
    private static void RegisterStringFields()
    {
        // String.Empty is implemented as a property in korlib (returns ""),
        // but C# compilers generate ldsfld for string.Empty expecting a field.
        // We create an empty string instance and register it.

        // Allocate storage for String.Empty (1 string pointer = 8 bytes)
        nint* stringStaticStorage = (nint*)Memory.HeapAllocator.AllocZeroed(8);
        if (stringStaticStorage == null) return;

        // Create an empty string instance
        string emptyStr = "";

        // Store the object reference
        stringStaticStorage[0] = *(nint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref emptyStr);

        Register("System.String", "Empty", (nint)(&stringStaticStorage[0]), 8, false);
    }
}
