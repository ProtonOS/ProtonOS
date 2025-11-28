// netos mernel - PAL Memory APIs
// Win32-style HeapAlloc/HeapFree and VirtualAlloc/VirtualFree for PAL compatibility.

using System.Runtime.InteropServices;
using Mernel.X64;

namespace Mernel;

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
/// Kernel heap handle structure.
/// Allows multiple heaps with different characteristics.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct KernelHeap
{
    public uint Magic;           // Validation magic
    public uint Flags;           // Heap flags
    public KernelSpinLock Lock;  // Thread safety
    public void* BaseAddress;    // Base of heap region (for private heaps)
    public ulong Size;           // Size of heap region
    public ulong Allocated;      // Currently allocated bytes
    public bool IsProcessHeap;   // True if this is the default process heap

    public const uint MagicValue = 0x48454150;  // "HEAP"
}

/// <summary>
/// Virtual memory allocation tracking.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VirtualAllocation
{
    public ulong BaseAddress;     // Virtual address
    public ulong Size;            // Allocated size
    public uint AllocationType;   // MEM_COMMIT, MEM_RESERVE
    public uint Protection;       // PAGE_READWRITE, etc.
    public VirtualAllocation* Next;
}

/// <summary>
/// PAL Memory APIs - Win32-compatible heap and virtual memory functions.
/// </summary>
public static unsafe class KernelMemoryOps
{
    // Process heap (default heap - uses the kernel HeapAllocator)
    private static KernelHeap _processHeap;
    private static bool _initialized;

    // Virtual allocation tracking
    private static VirtualAllocation* _virtualAllocList;
    private static KernelSpinLock _virtualAllocLock;

    // Next virtual address for allocations
    private static ulong _nextVirtualAddress = 0x0000_0002_0000_0000; // Start at 8GB

    /// <summary>
    /// Initialize the memory subsystem.
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        // Set up the process heap (uses kernel HeapAllocator)
        _processHeap.Magic = KernelHeap.MagicValue;
        _processHeap.Flags = HeapFlags.HEAP_GROWABLE;
        _processHeap.Lock = default;
        _processHeap.BaseAddress = null;
        _processHeap.Size = 0;
        _processHeap.Allocated = 0;
        _processHeap.IsProcessHeap = true;

        _virtualAllocList = null;
        _virtualAllocLock = default;

        _initialized = true;
    }

    // ==================== Heap APIs ====================

    /// <summary>
    /// Get the default process heap.
    /// </summary>
    public static KernelHeap* GetProcessHeap()
    {
        if (!_initialized) Init();
        fixed (KernelHeap* heap = &_processHeap)
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
    public static KernelHeap* HeapCreate(uint flOptions, ulong dwInitialSize, ulong dwMaximumSize)
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
        var heapPtr = (KernelHeap*)HeapAllocator.AllocZeroed((ulong)sizeof(KernelHeap));
        if (heapPtr == null) return null;

        heapPtr->Magic = KernelHeap.MagicValue;
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
    public static bool HeapDestroy(KernelHeap* hHeap)
    {
        if (hHeap == null || hHeap->Magic != KernelHeap.MagicValue)
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
    public static void* HeapAlloc(KernelHeap* hHeap, uint dwFlags, ulong dwBytes)
    {
        if (hHeap == null || hHeap->Magic != KernelHeap.MagicValue)
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
    public static bool HeapFree(KernelHeap* hHeap, uint dwFlags, void* lpMem)
    {
        if (hHeap == null || hHeap->Magic != KernelHeap.MagicValue)
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
    public static void* HeapReAlloc(KernelHeap* hHeap, uint dwFlags, void* lpMem, ulong dwBytes)
    {
        if (hHeap == null || hHeap->Magic != KernelHeap.MagicValue)
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
    public static ulong HeapSize(KernelHeap* hHeap, uint dwFlags, void* lpMem)
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

        // Round up to page boundary
        dwSize = (dwSize + VirtualMemory.PageSize - 1) & ~(VirtualMemory.PageSize - 1);

        ulong virtAddr;
        if (lpAddress != null)
        {
            // Use requested address (must be page-aligned)
            virtAddr = (ulong)lpAddress;
            if ((virtAddr & (VirtualMemory.PageSize - 1)) != 0)
                return null; // Not page-aligned
        }
        else
        {
            // Allocate from next available address
            _virtualAllocLock.Acquire();
            virtAddr = _nextVirtualAddress;
            _nextVirtualAddress += dwSize;
            _virtualAllocLock.Release();
        }

        // Convert protection flags to page flags
        ulong pageFlags = ProtectionToPageFlags(flProtect);

        // If committing, actually allocate physical pages and map them
        if ((flAllocationType & MemoryAllocationType.MEM_COMMIT) != 0)
        {
            ulong numPages = dwSize / VirtualMemory.PageSize;

            for (ulong i = 0; i < numPages; i++)
            {
                // Allocate physical page
                ulong physPage = PageAllocator.AllocatePage();
                if (physPage == 0)
                {
                    // Allocation failed - unmap what we've done so far
                    // (simplified - real implementation would clean up properly)
                    return null;
                }

                // Zero the page
                byte* pagePtr = (byte*)physPage;
                for (int j = 0; j < (int)VirtualMemory.PageSize; j++)
                    pagePtr[j] = 0;

                // Map virtual to physical
                ulong virt = virtAddr + i * VirtualMemory.PageSize;
                if (!VirtualMemory.MapPage(virt, physPage, pageFlags))
                {
                    PageAllocator.FreePage(physPage);
                    return null;
                }

                VirtualMemory.InvalidatePage(virt);
            }
        }

        // Track the allocation
        var alloc = (VirtualAllocation*)HeapAllocator.AllocZeroed((ulong)sizeof(VirtualAllocation));
        if (alloc != null)
        {
            alloc->BaseAddress = virtAddr;
            alloc->Size = dwSize;
            alloc->AllocationType = flAllocationType;
            alloc->Protection = flProtect;

            _virtualAllocLock.Acquire();
            alloc->Next = _virtualAllocList;
            _virtualAllocList = alloc;
            _virtualAllocLock.Release();
        }

        return (void*)virtAddr;
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

        ulong addr = (ulong)lpAddress;

        // Find the allocation
        _virtualAllocLock.Acquire();
        VirtualAllocation* prev = null;
        VirtualAllocation* current = _virtualAllocList;

        while (current != null)
        {
            if (current->BaseAddress == addr)
                break;
            prev = current;
            current = current->Next;
        }

        if (current == null)
        {
            _virtualAllocLock.Release();
            return false; // Not found
        }

        if ((dwFreeType & MemoryFreeType.MEM_RELEASE) != 0)
        {
            // Release the entire region
            dwSize = current->Size;

            // Remove from list
            if (prev != null)
                prev->Next = current->Next;
            else
                _virtualAllocList = current->Next;

            _virtualAllocLock.Release();

            // Free physical pages and unmap
            ulong numPages = dwSize / VirtualMemory.PageSize;
            for (ulong i = 0; i < numPages; i++)
            {
                ulong virt = addr + i * VirtualMemory.PageSize;
                // Note: We'd need to walk page tables to get physical address
                // For now, just invalidate the TLB entry
                VirtualMemory.InvalidatePage(virt);
            }

            HeapAllocator.Free(current);
            return true;
        }
        else if ((dwFreeType & MemoryFreeType.MEM_DECOMMIT) != 0)
        {
            // Decommit pages but keep reservation
            _virtualAllocLock.Release();

            // Round to page boundary
            dwSize = (dwSize + VirtualMemory.PageSize - 1) & ~(VirtualMemory.PageSize - 1);
            ulong numPages = dwSize / VirtualMemory.PageSize;

            for (ulong i = 0; i < numPages; i++)
            {
                ulong virt = addr + i * VirtualMemory.PageSize;
                VirtualMemory.InvalidatePage(virt);
            }

            return true;
        }

        _virtualAllocLock.Release();
        return false;
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
