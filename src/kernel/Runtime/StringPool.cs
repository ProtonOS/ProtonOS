// ProtonOS kernel - String Pool for interned strings
// Provides efficient string interning and ldstr caching.
// Strings in the pool live for the lifetime of the process.
//
// Design:
//   - Bump allocator for string storage (no deallocation)
//   - Hash table for lookup by content (String.Intern)
//   - Token cache for fast ldstr lookups (user string tokens)
//   - Acts as GC root via EnumerateRoots callback
//
// All strings added to the pool are kept alive because:
//   1. The pool's internal data structures reference them
//   2. GC.MarkRoots calls StringPool.EnumerateRoots to mark all interned strings

using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Threading;

namespace ProtonOS.Runtime;

/// <summary>
/// String pool for interned strings and ldstr caching.
/// Strings in this pool are never collected by GC.
/// </summary>
public static unsafe class StringPool
{
    // Pool configuration
    private const int InitialCapacity = 256;      // Initial hash table size
    private const int MaxTokenCacheSize = 1024;   // Max cached user string tokens

    // Hash table for string interning (content-based lookup)
    // Each bucket is a linked list of StringEntry pointers
    private static StringEntry** _buckets;
    private static int _bucketCount;
    private static int _entryCount;

    // Token cache for ldstr (token-based lookup)
    // Simple array indexed by token & mask for O(1) lookup
    private static TokenCacheEntry* _tokenCache;
    private static int _tokenCacheSize;
    private static int _tokenCacheMask;

    // Synchronization
    private static SpinLock _lock;
    private static bool _initialized;

    // Statistics
    private static ulong _lookups;
    private static ulong _hits;
    private static ulong _interned;

    /// <summary>
    /// Entry in the intern hash table.
    /// Stored in the kernel heap (not GC heap).
    /// </summary>
    private struct StringEntry
    {
        public void* StringObj;     // Pointer to the String object (in GCHeap)
        public uint Hash;           // Cached hash code
        public StringEntry* Next;   // Next entry in bucket chain
    }

    /// <summary>
    /// Entry in the token cache for ldstr.
    /// Maps user string token to cached String object.
    /// </summary>
    private struct TokenCacheEntry
    {
        public uint Token;          // User string token (0x70xxxxxx)
        public void* StringObj;     // Cached String object pointer
    }

    /// <summary>
    /// Initialize the string pool.
    /// Must be called after HeapAllocator.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        // Allocate hash table buckets
        _bucketCount = InitialCapacity;
        ulong bucketsSize = (ulong)(_bucketCount * sizeof(StringEntry*));
        _buckets = (StringEntry**)HeapAllocator.AllocZeroed(bucketsSize);
        if (_buckets == null)
        {
            DebugConsole.WriteLine("[StringPool] Failed to allocate hash table!");
            return false;
        }

        // Allocate token cache
        _tokenCacheSize = MaxTokenCacheSize;
        _tokenCacheMask = _tokenCacheSize - 1;  // Must be power of 2
        ulong cacheSize = (ulong)(_tokenCacheSize * sizeof(TokenCacheEntry));
        _tokenCache = (TokenCacheEntry*)HeapAllocator.AllocZeroed(cacheSize);
        if (_tokenCache == null)
        {
            DebugConsole.WriteLine("[StringPool] Failed to allocate token cache!");
            return false;
        }

        _entryCount = 0;
        _lookups = 0;
        _hits = 0;
        _interned = 0;

        _initialized = true;
        DebugConsole.WriteLine("[StringPool] Initialized");
        return true;
    }

    /// <summary>
    /// Get or create a cached string for a user string token (ldstr).
    /// Uses token-based cache for O(1) lookup.
    /// </summary>
    /// <param name="token">User string token (0x70xxxxxx)</param>
    /// <param name="root">Metadata root for string resolution</param>
    /// <returns>Pointer to cached String object, or null on failure</returns>
    public static void* GetOrCreateFromToken(uint token, ref MetadataRoot root)
    {
        if (!_initialized)
            return null;

        _lookups++;

        // Check token cache first (fast path)
        int cacheIndex = (int)(token & _tokenCacheMask);
        _lock.Acquire();

        if (_tokenCache[cacheIndex].Token == token && _tokenCache[cacheIndex].StringObj != null)
        {
            void* cached = _tokenCache[cacheIndex].StringObj;
            _lock.Release();
            _hits++;
            return cached;
        }

        _lock.Release();

        // Cache miss - allocate new string
        void* stringObj = MetadataReader.AllocateUserString(ref root, token);
        if (stringObj == null)
            return null;

        // Add to token cache
        _lock.Acquire();
        _tokenCache[cacheIndex].Token = token;
        _tokenCache[cacheIndex].StringObj = stringObj;
        _interned++;
        _lock.Release();

        // Also add to intern table for String.Intern lookups
        AddToInternTable(stringObj);

        return stringObj;
    }

    /// <summary>
    /// Intern a string: return existing interned string if content matches,
    /// otherwise add to the pool and return.
    /// This is the implementation of String.Intern.
    /// </summary>
    /// <param name="str">String object to intern</param>
    /// <returns>Interned string object (may be same or different from input)</returns>
    public static void* Intern(void* str)
    {
        if (!_initialized || str == null)
            return str;

        _lookups++;

        // Get string content for hashing
        int length = GetStringLength(str);
        ushort* chars = GetStringChars(str);
        uint hash = ComputeHash(chars, length);

        _lock.Acquire();

        // Look for existing interned string with same content
        int bucketIndex = (int)(hash % (uint)_bucketCount);
        StringEntry* entry = _buckets[bucketIndex];

        while (entry != null)
        {
            if (entry->Hash == hash && StringEquals(entry->StringObj, chars, length))
            {
                void* existing = entry->StringObj;
                _lock.Release();
                _hits++;
                return existing;
            }
            entry = entry->Next;
        }

        // Not found - add to intern table
        AddToInternTableLocked(str, hash, bucketIndex);
        _interned++;

        _lock.Release();
        return str;
    }

    /// <summary>
    /// Check if a string is already interned.
    /// </summary>
    public static void* IsInterned(void* str)
    {
        if (!_initialized || str == null)
            return null;

        int length = GetStringLength(str);
        ushort* chars = GetStringChars(str);
        uint hash = ComputeHash(chars, length);

        _lock.Acquire();

        int bucketIndex = (int)(hash % (uint)_bucketCount);
        StringEntry* entry = _buckets[bucketIndex];

        while (entry != null)
        {
            if (entry->Hash == hash && StringEquals(entry->StringObj, chars, length))
            {
                void* existing = entry->StringObj;
                _lock.Release();
                return existing;
            }
            entry = entry->Next;
        }

        _lock.Release();
        return null;
    }

    /// <summary>
    /// Enumerate all interned strings as GC roots.
    /// Called by GC.MarkRoots to keep interned strings alive.
    /// </summary>
    /// <param name="callback">Callback for each string reference slot</param>
    public static void EnumerateRoots(delegate*<void**, void> callback)
    {
        if (!_initialized || callback == null)
            return;

        _lock.Acquire();

        // Enumerate hash table entries
        for (int i = 0; i < _bucketCount; i++)
        {
            StringEntry* entry = _buckets[i];
            while (entry != null)
            {
                // Report the StringObj field as a root
                callback(&entry->StringObj);
                entry = entry->Next;
            }
        }

        // Enumerate token cache entries
        for (int i = 0; i < _tokenCacheSize; i++)
        {
            if (_tokenCache[i].StringObj != null)
            {
                callback(&_tokenCache[i].StringObj);
            }
        }

        _lock.Release();
    }

    /// <summary>
    /// Get the number of interned strings.
    /// </summary>
    public static int Count => _entryCount;

    /// <summary>
    /// Check if the pool is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Get pool statistics.
    /// </summary>
    public static void GetStats(out ulong lookups, out ulong hits, out ulong interned)
    {
        lookups = _lookups;
        hits = _hits;
        interned = _interned;
    }

    /// <summary>
    /// Add a string to the intern table (private helper).
    /// </summary>
    private static void AddToInternTable(void* str)
    {
        int length = GetStringLength(str);
        ushort* chars = GetStringChars(str);
        uint hash = ComputeHash(chars, length);

        _lock.Acquire();

        int bucketIndex = (int)(hash % (uint)_bucketCount);

        // Check if already present
        StringEntry* existing = _buckets[bucketIndex];
        while (existing != null)
        {
            if (existing->Hash == hash && StringEquals(existing->StringObj, chars, length))
            {
                _lock.Release();
                return;  // Already interned
            }
            existing = existing->Next;
        }

        AddToInternTableLocked(str, hash, bucketIndex);
        _lock.Release();
    }

    /// <summary>
    /// Add to intern table (must hold lock).
    /// </summary>
    private static void AddToInternTableLocked(void* str, uint hash, int bucketIndex)
    {
        // Allocate entry from kernel heap
        StringEntry* entry = (StringEntry*)HeapAllocator.Alloc((ulong)sizeof(StringEntry));
        if (entry == null)
        {
            DebugConsole.WriteLine("[StringPool] Failed to allocate entry!");
            return;
        }

        entry->StringObj = str;
        entry->Hash = hash;
        entry->Next = _buckets[bucketIndex];
        _buckets[bucketIndex] = entry;
        _entryCount++;

        // Check if we need to grow the table
        if (_entryCount > _bucketCount * 2)
        {
            GrowTable();
        }
    }

    /// <summary>
    /// Grow the hash table when load factor is too high.
    /// </summary>
    private static void GrowTable()
    {
        int newBucketCount = _bucketCount * 2;
        ulong newSize = (ulong)(newBucketCount * sizeof(StringEntry*));
        StringEntry** newBuckets = (StringEntry**)HeapAllocator.AllocZeroed(newSize);

        if (newBuckets == null)
            return;  // Keep using old table

        // Rehash all entries
        for (int i = 0; i < _bucketCount; i++)
        {
            StringEntry* entry = _buckets[i];
            while (entry != null)
            {
                StringEntry* next = entry->Next;

                int newIndex = (int)(entry->Hash % (uint)newBucketCount);
                entry->Next = newBuckets[newIndex];
                newBuckets[newIndex] = entry;

                entry = next;
            }
        }

        // Note: Old bucket array is leaked (no deallocation in kernel heap)
        // This is acceptable since growth is rare
        _buckets = newBuckets;
        _bucketCount = newBucketCount;
    }

    /// <summary>
    /// Compute hash code for string content.
    /// Uses FNV-1a hash.
    /// </summary>
    private static uint ComputeHash(ushort* chars, int length)
    {
        uint hash = 2166136261;  // FNV offset basis

        for (int i = 0; i < length; i++)
        {
            hash ^= chars[i];
            hash *= 16777619;  // FNV prime
        }

        return hash;
    }

    /// <summary>
    /// Get string length from String object.
    /// String layout: [MethodTable*][int _length][char _firstChar...]
    /// </summary>
    private static int GetStringLength(void* str)
    {
        return *(int*)((byte*)str + 8);
    }

    /// <summary>
    /// Get pointer to first char in String object.
    /// </summary>
    private static ushort* GetStringChars(void* str)
    {
        return (ushort*)((byte*)str + 12);
    }

    /// <summary>
    /// Compare string content.
    /// </summary>
    private static bool StringEquals(void* str, ushort* chars, int length)
    {
        if (str == null)
            return false;

        int strLen = GetStringLength(str);
        if (strLen != length)
            return false;

        ushort* strChars = GetStringChars(str);
        for (int i = 0; i < length; i++)
        {
            if (strChars[i] != chars[i])
                return false;
        }

        return true;
    }
}
