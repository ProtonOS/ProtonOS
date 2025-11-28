// netos mernel - PAL Memory APIs
// Win32-style HeapAlloc/HeapFree and VirtualAlloc/VirtualFree for PAL compatibility.
// These are PAL (Platform Abstraction Layer) wrappers over kernel memory services.

using System.Runtime.InteropServices;
using Kernel.Threading;
using Kernel.Memory;
using Kernel.X64;

namespace Kernel.PAL;

/// <summary>
/// Heap flags for HeapCreate and HeapAlloc.
/// </summary>
public static class HeapFlags
{
    public const uint HEAP_NO_SERIALIZE = 0x00000001;         // Not thread-safe (we ignore this)
    public const uint HEAP_GROWABLE = 0x00000002;             // Heap can grow
    public const uint HEAP_GENERATE_EXCEPTIONS = 0x00000004;  // Throw on failure (not supported)
    public const uint HEAP_ZERO_MEMORY = 0x00000008;          // Zero allocated memory
    public const uint HEAP_REALLOC_IN_PLACE_ONLY = 0x00000010;
    public const uint HEAP_CREATE_ENABLE_EXECUTE = 0x00040000;
}

/// <summary>
/// Memory protection constants for VirtualAlloc.
/// </summary>
public static class MemoryProtection
{
    public const uint PAGE_NOACCESS = 0x01;
    public const uint PAGE_READONLY = 0x02;
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_WRITECOPY = 0x08;
    public const uint PAGE_EXECUTE = 0x10;
    public const uint PAGE_EXECUTE_READ = 0x20;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;
    public const uint PAGE_EXECUTE_WRITECOPY = 0x80;
    public const uint PAGE_GUARD = 0x100;
    public const uint PAGE_NOCACHE = 0x200;
    public const uint PAGE_WRITECOMBINE = 0x400;
}

/// <summary>
/// Memory allocation type for VirtualAlloc.
/// </summary>
public static class MemoryAllocationType
{
    public const uint MEM_COMMIT = 0x00001000;
    public const uint MEM_RESERVE = 0x00002000;
    public const uint MEM_RESET = 0x00080000;
    public const uint MEM_TOP_DOWN = 0x00100000;
    public const uint MEM_WRITE_WATCH = 0x00200000;
    public const uint MEM_PHYSICAL = 0x00400000;
    public const uint MEM_LARGE_PAGES = 0x20000000;
}

/// <summary>
/// Memory free type for VirtualFree.
/// </summary>
public static class MemoryFreeType
{
    public const uint MEM_DECOMMIT = 0x00004000;
    public const uint MEM_RELEASE = 0x00008000;
}

/// <summary>
/// PAL heap handle structure.
/// Allows multiple heaps with different characteristics.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Heap
{
    public uint Magic;           // Validation magic
    public uint Flags;           // Heap flags
    public SpinLock Lock;  // Thread safety
    public void* BaseAddress;    // Base of heap region (for private heaps)
    public ulong Size;           // Size of heap region
    public ulong Allocated;      // Currently allocated bytes
    public bool IsProcessHeap;   // True if this is the default process heap

    public const uint MagicValue = 0x48454150;  // "HEAP"
}

/// <summary>
/// PAL Memory APIs - Win32-compatible heap and virtual memory functions.
/// These are thin wrappers over kernel services for PAL compatibility.
/// </summary>
public static unsafe class Memory
{
    // Process heap (default heap - uses the kernel HeapAllocator)
    private static Heap _processHeap;
    private static bool _initialized;

    /// <summary>
    /// Initialize the PAL memory subsystem.
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        // Set up the process heap (uses kernel HeapAllocator)
        _processHeap.Magic = Heap.MagicValue;
        _processHeap.Flags = HeapFlags.HEAP_GROWABLE;
        _processHeap.Lock = default;
        _processHeap.BaseAddress = null;
        _processHeap.Size = 0;
        _processHeap.Allocated = 0;
        _processHeap.IsProcessHeap = true;

        _initialized = true;
    }

    // ==================== Heap APIs ====================

    /// <summary>
    /// Get the default process heap.
    /// </summary>
    public static Heap* GetProcessHeap()
    {
        if (!_initialized) Init();
        fixed (Heap* heap = &_processHeap)
        {
            return heap;
        }
    }

    /// <summary>
    /// Create a new heap.
    /// </summary>
    /// <param name="flOptions">Heap options (HEAP_GROWABLE, etc.)</param>
    /// <param name="dwInitialSize">Initial size (0 for default)</param>
    /// <param name="dwMaximumSize">Maximum size (0 for growable)</param>
    /// <returns>Handle to the heap, or null on failure</returns>
    public static Heap* HeapCreate(uint flOptions, ulong dwInitialSize, ulong dwMaximumSize)
    {
        if (!_initialized) Init();

        // For simplicity, we only support the process heap for now
        // A full implementation would allocate a private heap region
        if (dwMaximumSize == 0)
        {
            // Growable heap - just return process heap
            return GetProcessHeap();
        }

        // Fixed-size heap - allocate a dedicated region
        var heapPtr = (Heap*)HeapAllocator.AllocZeroed((ulong)sizeof(Heap));
        if (heapPtr == null) return null;

        heapPtr->Magic = Heap.MagicValue;
        heapPtr->Flags = flOptions;
        heapPtr->Lock = default;
        heapPtr->IsProcessHeap = false;

        // Allocate the heap region
        ulong size = dwInitialSize > 0 ? dwInitialSize : 64 * 1024; // 64KB default
        heapPtr->BaseAddress = HeapAllocator.Alloc(size);
        if (heapPtr->BaseAddress == null)
        {
            HeapAllocator.Free(heapPtr);
            return null;
        }

        heapPtr->Size = size;
        heapPtr->Allocated = 0;

        return heapPtr;
    }

    /// <summary>
    /// Destroy a heap.
    /// </summary>
    public static bool HeapDestroy(Heap* hHeap)
    {
        if (hHeap == null || hHeap->Magic != Heap.MagicValue)
            return false;

        // Can't destroy the process heap
        if (hHeap->IsProcessHeap)
            return false;

        // Free the heap region
        if (hHeap->BaseAddress != null)
            HeapAllocator.Free(hHeap->BaseAddress);

        // Invalidate and free the heap structure
        hHeap->Magic = 0;
        HeapAllocator.Free(hHeap);

        return true;
    }

    /// <summary>
    /// Allocate memory from a heap.
    /// </summary>
    /// <param name="hHeap">Heap handle</param>
    /// <param name="dwFlags">Allocation flags</param>
    /// <param name="dwBytes">Number of bytes to allocate</param>
    /// <returns>Pointer to allocated memory, or null on failure</returns>
    public static void* HeapAlloc(Heap* hHeap, uint dwFlags, ulong dwBytes)
    {
        if (hHeap == null || hHeap->Magic != Heap.MagicValue)
            return null;

        if (dwBytes == 0)
            return null;

        void* ptr;

        if (hHeap->IsProcessHeap)
        {
            // Use kernel heap allocator
            hHeap->Lock.Acquire();

            if ((dwFlags & HeapFlags.HEAP_ZERO_MEMORY) != 0)
                ptr = HeapAllocator.AllocZeroed(dwBytes);
            else
                ptr = HeapAllocator.Alloc(dwBytes);

            if (ptr != null)
                hHeap->Allocated += dwBytes;

            hHeap->Lock.Release();
        }
        else
        {
            // Private heap - simple bump allocator for now
            hHeap->Lock.Acquire();

            if (hHeap->Allocated + dwBytes > hHeap->Size)
            {
                hHeap->Lock.Release();
                return null; // Out of space
            }

            ptr = (byte*)hHeap->BaseAddress + hHeap->Allocated;
            hHeap->Allocated += dwBytes;

            if ((dwFlags & HeapFlags.HEAP_ZERO_MEMORY) != 0)
            {
                byte* p = (byte*)ptr;
                for (ulong i = 0; i < dwBytes; i++)
                    p[i] = 0;
            }

            hHeap->Lock.Release();
        }

        return ptr;
    }

    /// <summary>
    /// Free memory allocated from a heap.
    /// </summary>
    public static bool HeapFree(Heap* hHeap, uint dwFlags, void* lpMem)
    {
        if (hHeap == null || hHeap->Magic != Heap.MagicValue)
            return false;

        if (lpMem == null)
            return true; // Freeing null is OK

        if (hHeap->IsProcessHeap)
        {
            hHeap->Lock.Acquire();
            HeapAllocator.Free(lpMem);
            hHeap->Lock.Release();
            return true;
        }
        else
        {
            // Private heaps with bump allocator don't support individual frees
            // This is a simplification - real Windows heaps track allocations
            return true;
        }
    }

    /// <summary>
    /// Reallocate memory from a heap.
    /// </summary>
    public static void* HeapReAlloc(Heap* hHeap, uint dwFlags, void* lpMem, ulong dwBytes)
    {
        if (hHeap == null || hHeap->Magic != Heap.MagicValue)
            return null;

        // Simple implementation: allocate new, copy, free old
        void* newPtr = HeapAlloc(hHeap, dwFlags, dwBytes);
        if (newPtr == null)
            return null;

        if (lpMem != null)
        {
            // Copy old data - we don't know the old size, so this is a simplification
            // In a real implementation, we'd track allocation sizes
            byte* src = (byte*)lpMem;
            byte* dst = (byte*)newPtr;
            for (ulong i = 0; i < dwBytes; i++)
                dst[i] = src[i];

            HeapFree(hHeap, 0, lpMem);
        }

        return newPtr;
    }

    /// <summary>
    /// Get the size of an allocated block.
    /// </summary>
    public static ulong HeapSize(Heap* hHeap, uint dwFlags, void* lpMem)
    {
        // This requires tracking allocation sizes - return 0 for now
        return 0;
    }

    // ==================== Virtual Memory APIs ====================

    /// <summary>
    /// Allocate or reserve virtual memory.
    /// </summary>
    /// <param name="lpAddress">Desired address (null for system choice)</param>
    /// <param name="dwSize">Size in bytes</param>
    /// <param name="flAllocationType">MEM_COMMIT, MEM_RESERVE</param>
    /// <param name="flProtect">PAGE_READWRITE, etc.</param>
    /// <returns>Base address of allocated region, or null on failure</returns>
    public static void* VirtualAlloc(void* lpAddress, ulong dwSize, uint flAllocationType, uint flProtect)
    {
        if (!_initialized) Init();

        if (dwSize == 0)
            return null;

        // Convert Win32 protection flags to kernel page flags
        ulong pageFlags = ProtectionToPageFlags(flProtect);

        // Determine if we should commit
        bool commit = (flAllocationType & MemoryAllocationType.MEM_COMMIT) != 0;

        // Call kernel VirtualMemory API
        ulong result = VirtualMemory.AllocateVirtualRange(
            (ulong)lpAddress,
            dwSize,
            commit,
            pageFlags);

        return (void*)result;
    }

    /// <summary>
    /// Free virtual memory.
    /// </summary>
    /// <param name="lpAddress">Base address of region</param>
    /// <param name="dwSize">Size (0 for MEM_RELEASE)</param>
    /// <param name="dwFreeType">MEM_RELEASE or MEM_DECOMMIT</param>
    public static bool VirtualFree(void* lpAddress, ulong dwSize, uint dwFreeType)
    {
        if (!_initialized) Init();

        if (lpAddress == null)
            return false;

        bool releaseAll = (dwFreeType & MemoryFreeType.MEM_RELEASE) != 0;

        return VirtualMemory.FreeVirtualRange((ulong)lpAddress, releaseAll);
    }

    /// <summary>
    /// Change protection on virtual memory pages.
    /// </summary>
    public static bool VirtualProtect(void* lpAddress, ulong dwSize, uint flNewProtect, uint* lpflOldProtect)
    {
        // Simplified - in a real implementation we'd modify page table entries
        if (lpflOldProtect != null)
            *lpflOldProtect = MemoryProtection.PAGE_READWRITE;

        return true;
    }

    /// <summary>
    /// Convert Win32 protection flags to x64 page flags.
    /// </summary>
    private static ulong ProtectionToPageFlags(uint protection)
    {
        ulong flags = PageFlags.Present;

        if ((protection & MemoryProtection.PAGE_NOACCESS) != 0)
            return 0; // Not present

        if ((protection & (MemoryProtection.PAGE_READWRITE |
                          MemoryProtection.PAGE_WRITECOPY |
                          MemoryProtection.PAGE_EXECUTE_READWRITE |
                          MemoryProtection.PAGE_EXECUTE_WRITECOPY)) != 0)
        {
            flags |= PageFlags.Writable;
        }

        if ((protection & (MemoryProtection.PAGE_EXECUTE |
                          MemoryProtection.PAGE_EXECUTE_READ |
                          MemoryProtection.PAGE_EXECUTE_READWRITE |
                          MemoryProtection.PAGE_EXECUTE_WRITECOPY)) == 0)
        {
            flags |= PageFlags.NoExecute;
        }

        if ((protection & MemoryProtection.PAGE_NOCACHE) != 0)
            flags |= PageFlags.CacheDisable;

        return flags;
    }
}
