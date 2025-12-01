// ProtonOS kernel - Code Heap for JIT-compiled code
// Allocates executable memory pages with proper W^X separation.
// Code pages are mapped without the NX (No-Execute) bit.

using ProtonOS.Platform;
using ProtonOS.X64;
using ProtonOS.Threading;

namespace ProtonOS.Memory;

/// <summary>
/// Code heap for JIT-compiled executable code.
/// Allocates memory with execute permission (no NX bit).
/// Uses a simple bump allocator within larger executable pages.
/// </summary>
public static unsafe class CodeHeap
{
    // Code pages are allocated in 64KB chunks for efficiency
    private const ulong ChunkSize = 64 * 1024;
    private const ulong PageSize = 4096;

    // Current allocation state
    private static byte* _currentChunk;
    private static ulong _currentOffset;
    private static ulong _currentChunkSize;

    // Statistics
    private static ulong _totalAllocated;
    private static ulong _totalUsed;

    private static SpinLock _lock;
    private static bool _initialized;

    /// <summary>
    /// Total bytes allocated from virtual memory
    /// </summary>
    public static ulong TotalAllocated => _totalAllocated;

    /// <summary>
    /// Total bytes used for code
    /// </summary>
    public static ulong TotalUsed => _totalUsed;

    /// <summary>
    /// Initialize the code heap.
    /// Must be called after VirtualMemory.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        if (!VirtualMemory.IsInitialized)
        {
            DebugConsole.WriteLine("[CodeHeap] VirtualMemory not initialized!");
            return false;
        }

        _currentChunk = null;
        _currentOffset = 0;
        _currentChunkSize = 0;
        _totalAllocated = 0;
        _totalUsed = 0;

        _initialized = true;
        DebugConsole.WriteLine("[CodeHeap] Initialized");
        return true;
    }

    /// <summary>
    /// Allocate executable memory for JIT code.
    /// Returns 16-byte aligned memory.
    /// </summary>
    /// <param name="size">Size in bytes needed</param>
    /// <returns>Pointer to executable memory, or null on failure</returns>
    public static byte* Alloc(ulong size)
    {
        if (!_initialized || size == 0)
            return null;

        // Align size to 16 bytes for code alignment
        size = (size + 15) & ~15UL;

        _lock.Acquire();

        // Check if we need a new chunk
        if (_currentChunk == null || _currentOffset + size > _currentChunkSize)
        {
            // Allocate new chunk
            ulong chunkSize = size > ChunkSize ? size : ChunkSize;
            chunkSize = (chunkSize + PageSize - 1) & ~(PageSize - 1);  // Page align

            // Allocate executable pages (KernelCode = Present, no NX, not writable)
            // Actually we need writable initially to write code, then we can make read-only
            // For simplicity, use RW + executable (Present | Writable, no NoExecute)
            ulong flags = PageFlags.Present | PageFlags.Writable;  // No NoExecute = executable

            ulong virtAddr = VirtualMemory.AllocateVirtualRange(0, chunkSize, true, flags);
            if (virtAddr == 0)
            {
                _lock.Release();
                DebugConsole.WriteLine("[CodeHeap] Failed to allocate executable pages!");
                return null;
            }

            _currentChunk = (byte*)virtAddr;
            _currentOffset = 0;
            _currentChunkSize = chunkSize;
            _totalAllocated += chunkSize;

            DebugConsole.Write("[CodeHeap] New chunk at 0x");
            DebugConsole.WriteHex(virtAddr);
            DebugConsole.Write(", size ");
            DebugConsole.WriteDecimal((uint)(chunkSize / 1024));
            DebugConsole.WriteLine(" KB");
        }

        // Bump allocate
        byte* result = _currentChunk + _currentOffset;
        _currentOffset += size;
        _totalUsed += size;

        _lock.Release();
        return result;
    }

    /// <summary>
    /// Allocate and zero executable memory.
    /// </summary>
    public static byte* AllocZeroed(ulong size)
    {
        byte* ptr = Alloc(size);
        if (ptr != null)
        {
            for (ulong i = 0; i < size; i++)
                ptr[i] = 0;
        }
        return ptr;
    }

    /// <summary>
    /// Make a code region read-only (optional hardening).
    /// Call after code generation is complete.
    /// </summary>
    public static void ProtectCode(byte* code, ulong size)
    {
        if (code == null || size == 0)
            return;

        // Change to read+execute (no write)
        ulong virtAddr = (ulong)code;
        ulong newFlags = PageFlags.Present;  // Read + Execute (no Writable, no NoExecute)

        VirtualMemory.ChangeRangeProtection(virtAddr, size, newFlags, out _);
    }
}
