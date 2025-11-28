// netos mernel - PAL Environment APIs
// Win32-compatible environment variable APIs for PAL compatibility.
// Used by CoreCLR for configuration and tuning.

using System.Runtime.InteropServices;
using Kernel.Memory;
using Kernel.Threading;

namespace Kernel.PAL;

/// <summary>
/// Environment variable entry - stored as wide string name=value.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct EnvEntry
{
    public char* Name;       // Heap-allocated wide string
    public char* Value;      // Heap-allocated wide string
    public int NameLength;   // Length in chars (excluding null)
    public int ValueLength;  // Length in chars (excluding null)
    public bool InUse;       // Whether this slot is occupied
}

/// <summary>
/// PAL Environment APIs - Win32-compatible environment variable functions.
/// Stores environment variables in a fixed-size table with heap-allocated strings.
/// </summary>
public static unsafe class EnvironmentApi
{
    private const int MaxEnvironmentVariables = 256;
    private const int MaxNameLength = 256;
    private const int MaxValueLength = 32768;  // Windows max is 32767

    private static EnvEntry* _entries;
    private static SpinLock _lock;
    private static bool _initialized;

    /// <summary>
    /// Initialize the environment variable storage.
    /// Called automatically on first use.
    /// </summary>
    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        _lock.Acquire();
        if (!_initialized)
        {
            // Allocate environment entry table
            nuint size = (nuint)(sizeof(EnvEntry) * MaxEnvironmentVariables);
            _entries = (EnvEntry*)HeapAllocator.AllocZeroed(size);
            _initialized = _entries != null;
        }
        _lock.Release();
    }

    /// <summary>
    /// Get the length of a wide string (null-terminated).
    /// </summary>
    private static int WideStringLength(char* str)
    {
        if (str == null)
            return 0;

        int len = 0;
        while (str[len] != '\0' && len < MaxValueLength)
            len++;
        return len;
    }

    /// <summary>
    /// Compare two wide strings case-insensitively.
    /// Returns true if equal.
    /// </summary>
    private static bool WideStringEqualsIgnoreCase(char* a, int aLen, char* b, int bLen)
    {
        if (aLen != bLen)
            return false;

        for (int i = 0; i < aLen; i++)
        {
            char ca = a[i];
            char cb = b[i];

            // Simple ASCII uppercase conversion
            if (ca >= 'a' && ca <= 'z')
                ca = (char)(ca - 32);
            if (cb >= 'a' && cb <= 'z')
                cb = (char)(cb - 32);

            if (ca != cb)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Copy a wide string.
    /// </summary>
    private static void WideStringCopy(char* dest, char* src, int length)
    {
        for (int i = 0; i < length; i++)
            dest[i] = src[i];
        dest[length] = '\0';
    }

    /// <summary>
    /// Find an environment variable by name.
    /// Returns the index or -1 if not found.
    /// Caller must hold the lock.
    /// </summary>
    private static int FindEntry(char* name, int nameLength)
    {
        if (_entries == null)
            return -1;

        for (int i = 0; i < MaxEnvironmentVariables; i++)
        {
            if (_entries[i].InUse &&
                WideStringEqualsIgnoreCase(_entries[i].Name, _entries[i].NameLength, name, nameLength))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Find an empty slot for a new entry.
    /// Returns the index or -1 if full.
    /// Caller must hold the lock.
    /// </summary>
    private static int FindEmptySlot()
    {
        if (_entries == null)
            return -1;

        for (int i = 0; i < MaxEnvironmentVariables; i++)
        {
            if (!_entries[i].InUse)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Get an environment variable value.
    /// </summary>
    /// <param name="lpName">Name of the environment variable (wide string)</param>
    /// <param name="lpBuffer">Buffer to receive the value</param>
    /// <param name="nSize">Size of buffer in characters</param>
    /// <returns>
    /// If successful and buffer is large enough: number of characters copied (excluding null)
    /// If buffer too small: required size including null terminator
    /// If not found: 0 (and SetLastError to ERROR_ENVVAR_NOT_FOUND)
    /// </returns>
    public static uint GetEnvironmentVariableW(char* lpName, char* lpBuffer, uint nSize)
    {
        if (lpName == null)
            return 0;

        EnsureInitialized();
        if (!_initialized)
            return 0;

        int nameLength = WideStringLength(lpName);
        if (nameLength == 0 || nameLength > MaxNameLength)
            return 0;

        _lock.Acquire();

        int index = FindEntry(lpName, nameLength);
        if (index < 0)
        {
            _lock.Release();
            // ERROR_ENVVAR_NOT_FOUND = 203
            return 0;
        }

        int valueLength = _entries[index].ValueLength;

        // If buffer is null or too small, return required size
        if (lpBuffer == null || nSize < (uint)(valueLength + 1))
        {
            _lock.Release();
            return (uint)(valueLength + 1);  // Include null terminator
        }

        // Copy value to buffer
        WideStringCopy(lpBuffer, _entries[index].Value, valueLength);

        _lock.Release();
        return (uint)valueLength;  // Return chars copied, excluding null
    }

    /// <summary>
    /// Set an environment variable.
    /// </summary>
    /// <param name="lpName">Name of the environment variable (wide string)</param>
    /// <param name="lpValue">Value to set, or null to delete the variable</param>
    /// <returns>True on success, false on failure</returns>
    public static bool SetEnvironmentVariableW(char* lpName, char* lpValue)
    {
        if (lpName == null)
            return false;

        EnsureInitialized();
        if (!_initialized)
            return false;

        int nameLength = WideStringLength(lpName);
        if (nameLength == 0 || nameLength > MaxNameLength)
            return false;

        _lock.Acquire();

        int existingIndex = FindEntry(lpName, nameLength);

        // If value is null, delete the variable
        if (lpValue == null)
        {
            if (existingIndex >= 0)
            {
                // Free the old strings
                if (_entries[existingIndex].Name != null)
                    HeapAllocator.Free(_entries[existingIndex].Name);
                if (_entries[existingIndex].Value != null)
                    HeapAllocator.Free(_entries[existingIndex].Value);

                _entries[existingIndex] = default;
            }
            _lock.Release();
            return true;
        }

        int valueLength = WideStringLength(lpValue);
        if (valueLength > MaxValueLength)
        {
            _lock.Release();
            return false;
        }

        int targetIndex;
        if (existingIndex >= 0)
        {
            // Update existing entry - free old value
            if (_entries[existingIndex].Value != null)
                HeapAllocator.Free(_entries[existingIndex].Value);
            targetIndex = existingIndex;
        }
        else
        {
            // Find empty slot for new entry
            targetIndex = FindEmptySlot();
            if (targetIndex < 0)
            {
                _lock.Release();
                return false;  // Table full
            }

            // Allocate and copy name
            nuint nameSize = (nuint)((nameLength + 1) * sizeof(char));
            char* newName = (char*)HeapAllocator.Alloc(nameSize);
            if (newName == null)
            {
                _lock.Release();
                return false;
            }
            WideStringCopy(newName, lpName, nameLength);
            _entries[targetIndex].Name = newName;
            _entries[targetIndex].NameLength = nameLength;
        }

        // Allocate and copy value
        nuint valueSize = (nuint)((valueLength + 1) * sizeof(char));
        char* newValue = (char*)HeapAllocator.Alloc(valueSize);
        if (newValue == null)
        {
            // If this was a new entry, clean up the name we allocated
            if (existingIndex < 0)
            {
                HeapAllocator.Free(_entries[targetIndex].Name);
                _entries[targetIndex] = default;
            }
            _lock.Release();
            return false;
        }
        WideStringCopy(newValue, lpValue, valueLength);
        _entries[targetIndex].Value = newValue;
        _entries[targetIndex].ValueLength = valueLength;
        _entries[targetIndex].InUse = true;

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Get all environment strings as a single block.
    /// Format: "name1=value1\0name2=value2\0...\0\0"
    /// </summary>
    /// <returns>Pointer to environment block, or null on failure.
    /// Caller must free with FreeEnvironmentStringsW.</returns>
    public static char* GetEnvironmentStringsW()
    {
        EnsureInitialized();
        if (!_initialized)
            return null;

        _lock.Acquire();

        // First pass: calculate total size needed
        nuint totalSize = 1;  // Final null terminator
        for (int i = 0; i < MaxEnvironmentVariables; i++)
        {
            if (_entries[i].InUse)
            {
                // name=value\0
                totalSize += (nuint)(_entries[i].NameLength + 1 + _entries[i].ValueLength + 1);
            }
        }

        // Allocate the block
        char* block = (char*)HeapAllocator.Alloc(totalSize * sizeof(char));
        if (block == null)
        {
            _lock.Release();
            return null;
        }

        // Second pass: copy entries
        char* p = block;
        for (int i = 0; i < MaxEnvironmentVariables; i++)
        {
            if (_entries[i].InUse)
            {
                // Copy name
                for (int j = 0; j < _entries[i].NameLength; j++)
                    *p++ = _entries[i].Name[j];

                // Add '='
                *p++ = '=';

                // Copy value
                for (int j = 0; j < _entries[i].ValueLength; j++)
                    *p++ = _entries[i].Value[j];

                // Add null terminator for this entry
                *p++ = '\0';
            }
        }

        // Final null terminator
        *p = '\0';

        _lock.Release();
        return block;
    }

    /// <summary>
    /// Free an environment block returned by GetEnvironmentStringsW.
    /// </summary>
    /// <param name="lpszEnvironmentBlock">Pointer to environment block</param>
    /// <returns>True on success</returns>
    public static bool FreeEnvironmentStringsW(char* lpszEnvironmentBlock)
    {
        if (lpszEnvironmentBlock == null)
            return false;

        HeapAllocator.Free(lpszEnvironmentBlock);
        return true;
    }

    /// <summary>
    /// Expand environment strings in a string (e.g., "%PATH%" -> value of PATH).
    /// This is a simplified implementation that only handles single %VAR% patterns.
    /// </summary>
    /// <param name="lpSrc">Source string with %VAR% patterns</param>
    /// <param name="lpDst">Destination buffer</param>
    /// <param name="nSize">Size of destination buffer in characters</param>
    /// <returns>Number of characters written, or required size if buffer too small</returns>
    public static uint ExpandEnvironmentStringsW(char* lpSrc, char* lpDst, uint nSize)
    {
        if (lpSrc == null)
            return 0;

        // For now, just copy the string without expansion
        // Full implementation would parse %VAR% patterns and substitute values
        int srcLen = WideStringLength(lpSrc);

        if (lpDst == null || nSize < (uint)(srcLen + 1))
            return (uint)(srcLen + 1);

        WideStringCopy(lpDst, lpSrc, srcLen);
        return (uint)(srcLen + 1);
    }
}
