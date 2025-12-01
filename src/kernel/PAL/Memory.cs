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
/// Memory state constants for VirtualQuery.
/// </summary>
public static class MemoryState
{
    public const uint MEM_COMMIT = 0x00001000;   // Pages are committed (backed by physical memory)
    public const uint MEM_RESERVE = 0x00002000;  // Pages are reserved (address space only)
    public const uint MEM_FREE = 0x00010000;     // Pages are free (not reserved or committed)
}

/// <summary>
/// Memory type constants for VirtualQuery.
/// </summary>
public static class MemoryType
{
    public const uint MEM_PRIVATE = 0x00020000;  // Private memory
    public const uint MEM_MAPPED = 0x00040000;   // Mapped view
    public const uint MEM_IMAGE = 0x01000000;    // Mapped image (executable)
}

/// <summary>
/// MEMORY_BASIC_INFORMATION structure for VirtualQuery.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MemoryBasicInformation
{
    public nuint BaseAddress;          // Base address of the region
    public nuint AllocationBase;       // Base address of the allocation
    public uint AllocationProtect;     // Initial protection when allocated
    public nuint RegionSize;           // Size of the region
    public uint State;                 // MEM_COMMIT, MEM_RESERVE, or MEM_FREE
    public uint Protect;               // Current protection
    public uint Type;                  // MEM_PRIVATE, MEM_MAPPED, or MEM_IMAGE
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
    /// Attempts in-place resize when possible for efficiency.
    /// </summary>
    public static void* HeapReAlloc(Heap* hHeap, uint dwFlags, void* lpMem, ulong dwBytes)
    {
        if (hHeap == null || hHeap->Magic != Heap.MagicValue)
            return null;

        // Get old usable size before realloc (needed for HEAP_ZERO_MEMORY)
        // This is the *usable* size, which may be larger than what was requested
        ulong oldUsableSize = lpMem != null ? HeapAllocator.GetSize(lpMem) : 0;

        // Use the efficient Realloc which attempts in-place resize
        void* result = HeapAllocator.Realloc(lpMem, dwBytes);

        if (result == null)
            return null;

        // Handle HEAP_ZERO_MEMORY flag for growth case
        // We zero from the old usable size to the new requested size
        // This may leave some bytes unzeroed if the old block was larger
        // than what was originally requested, but that matches Win32 behavior
        // where HEAP_ZERO_MEMORY zeroes "additional memory" beyond the original
        if ((dwFlags & HeapFlags.HEAP_ZERO_MEMORY) != 0 && dwBytes > oldUsableSize)
        {
            // Zero the portion beyond the old usable size
            CPU.MemZero((byte*)result + oldUsableSize, dwBytes - oldUsableSize);
        }

        return result;
    }

    /// <summary>
    /// Get the size of an allocated block.
    /// </summary>
    public static ulong HeapSize(Heap* hHeap, uint dwFlags, void* lpMem)
    {
        // We use a single process heap, so ignore hHeap
        // dwFlags is unused (HEAP_NO_SERIALIZE not relevant in kernel)
        return HeapAllocator.GetSize(lpMem);
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
        if (!_initialized) Init();

        if (lpAddress == null || dwSize == 0)
            return false;

        // Convert Win32 protection to page flags
        ulong newPageFlags = ProtectionToPageFlags(flNewProtect);

        // Change the protection
        ulong oldPageFlags;
        bool success = VirtualMemory.ChangeRangeProtection((ulong)lpAddress, dwSize, newPageFlags, out oldPageFlags);

        if (success && lpflOldProtect != null)
        {
            // Convert old page flags back to Win32 format
            *lpflOldProtect = PageFlagsToProtection(oldPageFlags);
        }

        return success;
    }

    /// <summary>
    /// Convert x64 page flags to Win32 protection flags.
    /// </summary>
    private static uint PageFlagsToProtection(ulong pageFlags)
    {
        if ((pageFlags & PageFlags.Present) == 0)
            return MemoryProtection.PAGE_NOACCESS;

        bool writable = (pageFlags & PageFlags.Writable) != 0;
        bool executable = (pageFlags & PageFlags.NoExecute) == 0;

        if (executable && writable)
            return MemoryProtection.PAGE_EXECUTE_READWRITE;
        if (executable)
            return MemoryProtection.PAGE_EXECUTE_READ;
        if (writable)
            return MemoryProtection.PAGE_READWRITE;

        return MemoryProtection.PAGE_READONLY;
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

    /// <summary>
    /// Query information about a range of virtual memory.
    /// </summary>
    /// <param name="lpAddress">Address to query</param>
    /// <param name="lpBuffer">Pointer to MEMORY_BASIC_INFORMATION to receive info</param>
    /// <param name="dwLength">Size of buffer</param>
    /// <returns>Number of bytes written to buffer, or 0 on failure</returns>
    public static ulong VirtualQuery(void* lpAddress, MemoryBasicInformation* lpBuffer, ulong dwLength)
    {
        if (!_initialized) Init();

        if (lpBuffer == null || dwLength < (ulong)sizeof(MemoryBasicInformation))
            return 0;

        ulong queryAddr = (ulong)lpAddress;
        ulong pageAligned = queryAddr & ~(VirtualMemory.PageSize - 1);

        // Try to find this address in our allocation table
        ulong allocBase = VirtualMemory.FindAllocation(queryAddr, out ulong allocSize);

        if (allocBase != 0)
        {
            // Address is within a known allocation
            lpBuffer->AllocationBase = (nuint)allocBase;
            lpBuffer->BaseAddress = (nuint)pageAligned;
            lpBuffer->RegionSize = (nuint)(allocBase + allocSize - pageAligned);
            lpBuffer->Type = MemoryType.MEM_PRIVATE;

            // Check if pages are actually mapped (committed)
            ulong pageEntry = VirtualMemory.GetPageEntry(pageAligned);
            if ((pageEntry & PageFlags.Present) != 0)
            {
                lpBuffer->State = MemoryState.MEM_COMMIT;
                lpBuffer->Protect = PageFlagsToProtection(pageEntry);
            }
            else
            {
                lpBuffer->State = MemoryState.MEM_RESERVE;
                lpBuffer->Protect = MemoryProtection.PAGE_NOACCESS;
            }

            // Initial protection is same as current for our simple implementation
            lpBuffer->AllocationProtect = lpBuffer->Protect;

            return (ulong)sizeof(MemoryBasicInformation);
        }

        // Not in allocation table - check if it's mapped anyway (kernel memory, etc.)
        ulong pageEntry2 = VirtualMemory.GetPageEntry(pageAligned);
        if ((pageEntry2 & PageFlags.Present) != 0)
        {
            // Page is mapped but not in our allocation table (kernel memory)
            lpBuffer->BaseAddress = (nuint)pageAligned;
            lpBuffer->AllocationBase = (nuint)pageAligned;  // Unknown allocation base
            lpBuffer->RegionSize = (nuint)VirtualMemory.PageSize;
            lpBuffer->State = MemoryState.MEM_COMMIT;
            lpBuffer->Protect = PageFlagsToProtection(pageEntry2);
            lpBuffer->AllocationProtect = lpBuffer->Protect;
            lpBuffer->Type = MemoryType.MEM_PRIVATE;

            return (ulong)sizeof(MemoryBasicInformation);
        }

        // Address is free (not reserved or committed)
        lpBuffer->BaseAddress = (nuint)pageAligned;
        lpBuffer->AllocationBase = 0;
        lpBuffer->AllocationProtect = 0;
        lpBuffer->RegionSize = (nuint)VirtualMemory.PageSize;  // Minimum region size
        lpBuffer->State = MemoryState.MEM_FREE;
        lpBuffer->Protect = MemoryProtection.PAGE_NOACCESS;
        lpBuffer->Type = 0;

        return (ulong)sizeof(MemoryBasicInformation);
    }

    // ========================================================================
    // Runtime Heap Exports for netlib
    // These are exported for netlib's RhpNewFast/RhpNewArray to use
    // ========================================================================

    /// <summary>
    /// Allocate zeroed memory for managed object allocation.
    /// Exported for netlib's StartupCodeHelpers.AllocObject.
    /// Uses GCHeap when available (proper object header), falls back to HeapAllocator during early boot.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "PalAllocObject")]
    public static void* AllocObject(uint size)
    {
        // Use GC heap if initialized (has proper object header for GC)
        if (GCHeap.IsInitialized)
            return GCHeap.AllocZeroed(size);

        // Fall back to kernel heap during early boot (before GCHeap.Init)
        return HeapAllocator.AllocZeroed(size);
    }
}
