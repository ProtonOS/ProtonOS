// ProtonOS kernel - PAL Thread Local Storage (TLS)
// Win32-style TLS implementation for PAL compatibility.
// Supports TlsAlloc, TlsFree, TlsGetValue, TlsSetValue.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.X64;

namespace ProtonOS.PAL;

/// <summary>
/// PAL Thread Local Storage management.
/// Provides Win32-compatible TLS APIs needed by CoreCLR PAL.
/// </summary>
public static unsafe class TLS
{
    private const uint MaxTlsSlots = 1088;  // Win32 has 64 static + 1024 dynamic = 1088 total
    private const uint InvalidTlsIndex = 0xFFFFFFFF;

    private static SpinLock _lock;
    private static ulong* _slotBitmap;      // Bitmap tracking allocated slots
    private static uint _bitmapSize;         // Number of ulong elements in bitmap
    private static bool _initialized;

    /// <summary>
    /// Initialize the TLS subsystem.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        // Allocate bitmap for tracking TLS slots (1088 slots = 17 ulongs = 136 bytes)
        _bitmapSize = (MaxTlsSlots + 63) / 64;
        _slotBitmap = (ulong*)HeapAllocator.AllocZeroed(_bitmapSize * sizeof(ulong));
        if (_slotBitmap == null)
        {
            DebugConsole.WriteLine("[TLS] Failed to allocate slot bitmap!");
            return;
        }

        _initialized = true;
        DebugConsole.Write("[TLS] Initialized, max slots: ");
        DebugConsole.WriteHex((ushort)MaxTlsSlots);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Allocate a TLS slot index.
    /// Returns TLS_OUT_OF_INDEXES (0xFFFFFFFF) on failure.
    /// </summary>
    public static uint TlsAlloc()
    {
        if (!_initialized)
            return InvalidTlsIndex;

        _lock.Acquire();

        // Find first free slot in bitmap
        for (uint i = 0; i < _bitmapSize; i++)
        {
            if (_slotBitmap[i] != 0xFFFFFFFFFFFFFFFF)
            {
                // Found a ulong with a free bit
                ulong bits = _slotBitmap[i];
                for (int bit = 0; bit < 64; bit++)
                {
                    uint slotIndex = i * 64 + (uint)bit;
                    if (slotIndex >= MaxTlsSlots)
                        break;

                    if ((bits & (1UL << bit)) == 0)
                    {
                        // Mark slot as allocated
                        _slotBitmap[i] |= (1UL << bit);
                        _lock.Release();
                        return slotIndex;
                    }
                }
            }
        }

        _lock.Release();
        return InvalidTlsIndex;  // No free slots
    }

    /// <summary>
    /// Free a TLS slot index.
    /// Clears the value for all threads by enumerating them.
    /// </summary>
    public static bool TlsFree(uint dwTlsIndex)
    {
        if (!_initialized || dwTlsIndex >= MaxTlsSlots)
            return false;

        _lock.Acquire();

        uint wordIndex = dwTlsIndex / 64;
        int bitIndex = (int)(dwTlsIndex % 64);

        // Check if slot was allocated
        if ((_slotBitmap[wordIndex] & (1UL << bitIndex)) == 0)
        {
            _lock.Release();
            return false;  // Slot wasn't allocated
        }

        // Mark slot as free
        _slotBitmap[wordIndex] &= ~(1UL << bitIndex);

        // Clear the slot value for all threads by enumerating them
        ref var schedLock = ref Scheduler.SchedulerLock;
        schedLock.Acquire();
        for (var t = Scheduler.AllThreadsHead; t != null; t = t->NextAll)
        {
            if (t->TlsSlots != null && dwTlsIndex < t->TlsSlotCount)
            {
                t->TlsSlots[dwTlsIndex] = null;
            }
        }
        schedLock.Release();

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Get the value in the current thread's TLS slot.
    /// Returns null if the slot has never been set.
    /// </summary>
    public static void* TlsGetValue(uint dwTlsIndex)
    {
        if (!_initialized || dwTlsIndex >= MaxTlsSlots)
            return null;

        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return null;

        // Check if thread has TLS slots allocated
        if (thread->TlsSlots == null || dwTlsIndex >= thread->TlsSlotCount)
            return null;

        return thread->TlsSlots[dwTlsIndex];
    }

    /// <summary>
    /// Set the value in the current thread's TLS slot.
    /// Allocates TLS storage for the thread if not already allocated.
    /// </summary>
    public static bool TlsSetValue(uint dwTlsIndex, void* lpTlsValue)
    {
        if (!_initialized || dwTlsIndex >= MaxTlsSlots)
            return false;

        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return false;

        // Verify the slot is actually allocated globally
        _lock.Acquire();
        uint wordIndex = dwTlsIndex / 64;
        int bitIndex = (int)(dwTlsIndex % 64);
        bool slotAllocated = (_slotBitmap[wordIndex] & (1UL << bitIndex)) != 0;
        _lock.Release();

        if (!slotAllocated)
            return false;

        // Allocate TLS slots for this thread if needed
        if (thread->TlsSlots == null || dwTlsIndex >= thread->TlsSlotCount)
        {
            if (!GrowThreadTlsSlots(thread, dwTlsIndex + 1))
                return false;
        }

        thread->TlsSlots[dwTlsIndex] = lpTlsValue;
        return true;
    }

    /// <summary>
    /// Grow a thread's TLS slot array to accommodate at least the given number of slots.
    /// Uses HeapAllocator.Realloc for efficient in-place growth when possible.
    /// </summary>
    private static bool GrowThreadTlsSlots(Thread* thread, uint minSlots)
    {
        // Round up to next power of 2, minimum 64
        uint newCount = 64;
        while (newCount < minSlots && newCount < MaxTlsSlots)
            newCount *= 2;
        if (newCount > MaxTlsSlots)
            newCount = MaxTlsSlots;

        ulong newSize = newCount * (uint)sizeof(void*);
        ulong oldSize = thread->TlsSlotCount * (uint)sizeof(void*);

        // Use Realloc - attempts in-place growth, falls back to copy if needed
        var newSlots = (void**)HeapAllocator.Realloc(thread->TlsSlots, newSize);
        if (newSlots == null)
            return false;

        // Zero the new portion (Realloc doesn't zero new memory)
        if (newSize > oldSize)
        {
            CPU.MemZero((byte*)newSlots + oldSize, newSize - oldSize);
        }

        thread->TlsSlots = newSlots;
        thread->TlsSlotCount = newCount;
        return true;
    }

    /// <summary>
    /// Clean up TLS storage for a thread (called when thread exits).
    /// </summary>
    public static void CleanupThread(Thread* thread)
    {
        if (thread == null)
            return;

        if (thread->TlsSlots != null)
        {
            HeapAllocator.Free(thread->TlsSlots);
            thread->TlsSlots = null;
            thread->TlsSlotCount = 0;
        }
    }
}
