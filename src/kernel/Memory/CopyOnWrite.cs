// ProtonOS kernel - Copy-on-Write Page Management
// Tracks shared pages and handles COW page faults for fork.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.X64;
using ProtonOS.Process;

namespace ProtonOS.Memory;

/// <summary>
/// Copy-on-Write page management for fork support
/// </summary>
public static unsafe class CopyOnWrite
{
    // Reference count array (indexed by page frame number)
    // Only tracks pages with refcount >= 2 (shared)
    private static CowEntry* _refCounts;
    private static int _maxEntries;
    private static int _usedEntries;
    private static SpinLock _lock;
    private static bool _initialized;

    // Use a hash table for sparse reference counting
    private const int HashTableSize = 4096;
    private static CowEntry** _hashTable;

    /// <summary>
    /// Initialize COW tracking
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[COW] Initializing copy-on-write tracking...");

        // Allocate hash table
        _hashTable = (CowEntry**)HeapAllocator.AllocZeroed((ulong)(sizeof(CowEntry*) * HashTableSize));
        if (_hashTable == null)
        {
            DebugConsole.WriteLine("[COW] Failed to allocate hash table!");
            return;
        }

        _maxEntries = 0;
        _usedEntries = 0;
        _initialized = true;

        DebugConsole.WriteLine("[COW] Initialized");
    }

    /// <summary>
    /// Hash function for physical addresses
    /// </summary>
    private static int Hash(ulong physAddr)
    {
        ulong pfn = physAddr >> 12;
        return (int)(pfn % HashTableSize);
    }

    /// <summary>
    /// Find or create a COW entry for a physical page
    /// </summary>
    private static CowEntry* FindOrCreate(ulong physAddr, bool create)
    {
        int idx = Hash(physAddr);
        CowEntry* entry = _hashTable[idx];

        while (entry != null)
        {
            if (entry->PhysAddr == physAddr)
                return entry;
            entry = entry->Next;
        }

        if (!create)
            return null;

        // Create new entry
        entry = (CowEntry*)HeapAllocator.AllocZeroed((ulong)sizeof(CowEntry));
        if (entry == null)
            return null;

        entry->PhysAddr = physAddr;
        entry->RefCount = 1;
        entry->Next = _hashTable[idx];
        _hashTable[idx] = entry;
        _usedEntries++;

        return entry;
    }

    /// <summary>
    /// Mark a page as shared (increment reference count)
    /// Called when creating COW mapping during fork
    /// </summary>
    public static void MarkShared(ulong physAddr)
    {
        if (!_initialized)
            Init();

        _lock.Acquire();

        var entry = FindOrCreate(physAddr, true);
        if (entry != null)
        {
            entry->RefCount++;
        }

        _lock.Release();
    }

    /// <summary>
    /// Increment reference count for a page
    /// </summary>
    public static void IncrementRef(ulong physAddr)
    {
        if (!_initialized)
            Init();

        _lock.Acquire();

        var entry = FindOrCreate(physAddr, true);
        if (entry != null)
        {
            entry->RefCount++;
        }

        _lock.Release();
    }

    /// <summary>
    /// Decrement reference count for a page
    /// Returns new reference count
    /// </summary>
    public static int DecrementRef(ulong physAddr)
    {
        if (!_initialized)
            return 0;

        _lock.Acquire();

        var entry = FindOrCreate(physAddr, false);
        if (entry == null)
        {
            _lock.Release();
            return 0;
        }

        entry->RefCount--;
        int newCount = entry->RefCount;

        // Remove entry if no longer shared
        if (newCount <= 1)
        {
            RemoveEntry(physAddr);
        }

        _lock.Release();
        return newCount;
    }

    /// <summary>
    /// Remove a COW entry from the hash table
    /// Must be called with lock held
    /// </summary>
    private static void RemoveEntry(ulong physAddr)
    {
        int idx = Hash(physAddr);
        CowEntry* prev = null;
        CowEntry* entry = _hashTable[idx];

        while (entry != null)
        {
            if (entry->PhysAddr == physAddr)
            {
                if (prev != null)
                    prev->Next = entry->Next;
                else
                    _hashTable[idx] = entry->Next;

                HeapAllocator.Free(entry);
                _usedEntries--;
                return;
            }
            prev = entry;
            entry = entry->Next;
        }
    }

    /// <summary>
    /// Check if a page is shared (COW)
    /// </summary>
    public static bool IsShared(ulong physAddr)
    {
        if (!_initialized)
            return false;

        _lock.Acquire();

        var entry = FindOrCreate(physAddr, false);
        bool shared = entry != null && entry->RefCount > 1;

        _lock.Release();
        return shared;
    }

    /// <summary>
    /// Get the reference count for a page
    /// </summary>
    public static int GetRefCount(ulong physAddr)
    {
        if (!_initialized)
            return 1;

        _lock.Acquire();

        var entry = FindOrCreate(physAddr, false);
        int count = entry != null ? entry->RefCount : 1;

        _lock.Release();
        return count;
    }

    /// <summary>
    /// Handle a page fault for a COW page
    /// Returns true if handled, false if not a COW fault
    /// </summary>
    /// <param name="faultAddr">Virtual address that caused the fault</param>
    /// <param name="proc">Process that faulted</param>
    /// <returns>True if COW was handled, false otherwise</returns>
    public static bool HandlePageFault(ulong faultAddr, Process.Process* proc)
    {
        if (proc == null || proc->PageTableRoot == 0)
            return false;

        // Get the current page entry
        ulong pml4Phys = proc->PageTableRoot;
        ulong oldPhysAddr = AddressSpace.GetPhysicalAddress(pml4Phys, faultAddr);

        if (oldPhysAddr == 0)
            return false;

        // Check if this page is COW shared
        if (!IsShared(oldPhysAddr))
            return false;

        // Allocate a new page
        ulong newPhysAddr = PageAllocator.AllocatePage();
        if (newPhysAddr == 0)
            return false;

        // Copy content from old page to new page
        byte* src = (byte*)oldPhysAddr;
        byte* dst = (byte*)newPhysAddr;
        for (int i = 0; i < 4096; i++)
            dst[i] = src[i];

        // Decrement reference count on old page
        int newRefCount = DecrementRef(oldPhysAddr);

        // If old page is no longer shared, we could free it
        // but it might still be in use by parent/another process

        // Update page table to point to new page with write permission
        ulong pageAligned = faultAddr & ~0xFFFUL;
        ulong flags = PageFlags.Present | PageFlags.Writable | PageFlags.User;

        // Unmap old and map new
        AddressSpace.UnmapUserPage(pml4Phys, pageAligned);
        AddressSpace.MapUserPage(pml4Phys, pageAligned, newPhysAddr, flags);

        // Invalidate TLB entry
        CPU.Invlpg(faultAddr);

        return true;
    }

    /// <summary>
    /// Get statistics
    /// </summary>
    public static int SharedPageCount => _usedEntries;
}

/// <summary>
/// COW reference count entry
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CowEntry
{
    public ulong PhysAddr;
    public int RefCount;
    public CowEntry* Next;
}
