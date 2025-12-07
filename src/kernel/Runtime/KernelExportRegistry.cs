// ProtonOS kernel - Kernel Export Registry
// Maps kernel export entry point names to function addresses for PInvoke resolution.

using System;
using ProtonOS.Platform;

namespace ProtonOS.Runtime;

/// <summary>
/// Entry in the kernel export registry.
/// </summary>
public unsafe struct KernelExportEntry
{
    public byte* Name;       // Pointer to null-terminated ASCII string
    public void* Address;    // Function address
}

/// <summary>
/// Registry of kernel exports for PInvoke resolution.
/// Drivers with DllImport("*", EntryPoint = "Kernel_XXX") will be resolved through this registry.
/// </summary>
public static unsafe class KernelExportRegistry
{
    private const int MaxExports = 128;
    private static KernelExportEntry* _exports;
    private static int _exportCount;
    private static bool _initialized;

    /// <summary>
    /// Initialize the export registry.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // Allocate export array from heap
        _exports = (KernelExportEntry*)Memory.HeapAllocator.Alloc((ulong)(sizeof(KernelExportEntry) * MaxExports));
        _exportCount = 0;
        _initialized = true;
    }

    /// <summary>
    /// Register a kernel export.
    /// </summary>
    /// <param name="name">Pointer to null-terminated ASCII entry point name</param>
    /// <param name="address">Function address</param>
    public static void Register(byte* name, void* address)
    {
        if (!_initialized || _exports == null || _exportCount >= MaxExports)
            return;

        // Copy the name to heap memory since the source might be stack-allocated
        int len = 0;
        while (name[len] != 0) len++;
        len++; // Include null terminator

        byte* heapName = (byte*)Memory.HeapAllocator.Alloc((ulong)len);
        for (int i = 0; i < len; i++)
            heapName[i] = name[i];

        _exports[_exportCount].Name = heapName;
        _exports[_exportCount].Address = address;
        _exportCount++;
    }

    /// <summary>
    /// Register a kernel export using inline ASCII string.
    /// </summary>
    public static void Register(string name, void* address)
    {
        // Convert managed string to byte pointer
        // Note: For now, use the string's internal data directly
        // This works because our korlib strings are UTF-16
        // We need to compare as UTF-8/ASCII from metadata
        if (!_initialized || _exports == null || _exportCount >= MaxExports)
            return;

        // For static strings, we can get a pointer to the character data
        // and store the length along with the pointer
        fixed (char* chars = name)
        {
            _exports[_exportCount].Name = (byte*)chars;  // Will need UTF-16 comparison
            _exports[_exportCount].Address = address;
            _exportCount++;
        }
    }

    /// <summary>
    /// Look up a kernel export by entry point name.
    /// </summary>
    /// <param name="name">Pointer to null-terminated ASCII name</param>
    /// <returns>Function address, or null if not found</returns>
    public static void* Lookup(byte* name)
    {
        if (!_initialized || _exports == null || name == null)
            return null;

        for (int i = 0; i < _exportCount; i++)
        {
            if (StringEquals(_exports[i].Name, name))
                return _exports[i].Address;
        }

        return null;
    }

    /// <summary>
    /// Look up a kernel export by entry point name (UTF-8 string with length).
    /// </summary>
    public static void* LookupUtf8(byte* name, int length)
    {
        if (!_initialized || _exports == null || name == null)
            return null;

        for (int i = 0; i < _exportCount; i++)
        {
            if (StringEqualsUtf8(_exports[i].Name, name, length))
                return _exports[i].Address;
        }

        return null;
    }

    /// <summary>
    /// Compare two null-terminated ASCII strings for equality.
    /// </summary>
    private static bool StringEquals(byte* a, byte* b)
    {
        if (a == null || b == null)
            return false;

        while (*a != 0 && *b != 0)
        {
            if (*a != *b)
                return false;
            a++;
            b++;
        }

        return *a == *b;  // Both should be 0 for equality
    }

    /// <summary>
    /// Compare a UTF-16 string (in export registry) with a UTF-8 string (from metadata).
    /// The registry stores strings as char* (UTF-16), metadata uses UTF-8/ASCII.
    /// </summary>
    private static bool StringEqualsUtf8(byte* exportName, byte* utf8Name, int utf8Length)
    {
        if (exportName == null || utf8Name == null)
            return false;

        // Export names are stored as char* (UTF-16), cast to byte*
        // We need to compare as UTF-16 vs UTF-8
        char* utf16 = (char*)exportName;
        int i = 0;

        while (i < utf8Length && *utf16 != 0)
        {
            // For ASCII range, UTF-8 byte equals UTF-16 char value
            if ((char)utf8Name[i] != *utf16)
                return false;
            i++;
            utf16++;
        }

        // Both should end at the same point
        return i == utf8Length && *utf16 == 0;
    }

    /// <summary>
    /// Number of registered exports.
    /// </summary>
    public static int Count => _exportCount;

    /// <summary>
    /// Debug: print all registered exports.
    /// </summary>
    public static void DebugPrint()
    {
        DebugConsole.Write("[Exports] Registered ");
        DebugConsole.WriteDecimal((uint)_exportCount);
        DebugConsole.WriteLine(" kernel exports");
    }
}
