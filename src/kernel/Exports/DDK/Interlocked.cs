// ProtonOS kernel - Interlocked Exports
// Exposes atomic operations for System.Threading.Interlocked in korlib.

using System.Runtime.InteropServices;
using ProtonOS.X64;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// Kernel exports for atomic/interlocked operations.
/// Called by korlib System.Threading.Interlocked.
/// </summary>
public static unsafe class InterlockedExports
{
    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Increment32")]
    public static int Increment32(int* location)
    {
        // AtomicAdd returns original value, we need to return new value
        return CPU.AtomicAdd(ref *location, 1) + 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Decrement32")]
    public static int Decrement32(int* location)
    {
        return CPU.AtomicAdd(ref *location, -1) - 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Exchange32")]
    public static int Exchange32(int* location, int value)
    {
        return CPU.AtomicExchange(ref *location, value);
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_CompareExchange32")]
    public static int CompareExchange32(int* location, int value, int comparand)
    {
        return CPU.AtomicCompareExchange(ref *location, value, comparand);
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Add32")]
    public static int Add32(int* location, int value)
    {
        // AtomicAdd returns original, we return the new value
        return CPU.AtomicAdd(ref *location, value) + value;
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Increment64")]
    public static long Increment64(long* location)
    {
        return CPU.AtomicAdd64(ref *location, 1) + 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Decrement64")]
    public static long Decrement64(long* location)
    {
        return CPU.AtomicAdd64(ref *location, -1) - 1;
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Exchange64")]
    public static long Exchange64(long* location, long value)
    {
        return CPU.AtomicExchange64(ref *location, value);
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_CompareExchange64")]
    public static long CompareExchange64(long* location, long value, long comparand)
    {
        return CPU.AtomicCompareExchange64(ref *location, value, comparand);
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_Add64")]
    public static long Add64(long* location, long value)
    {
        return CPU.AtomicAdd64(ref *location, value) + value;
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_ExchangePointer")]
    public static void* ExchangePointer(void** location, void* value)
    {
        return CPU.AtomicExchangePointer(ref *location, value);
    }

    [UnmanagedCallersOnly(EntryPoint = "Interlocked_CompareExchangePointer")]
    public static void* CompareExchangePointer(void** location, void* value, void* comparand)
    {
        return CPU.AtomicCompareExchangePointer(ref *location, value, comparand);
    }
}
