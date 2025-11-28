// netos mernel - PAL Interlocked APIs
// Win32-compatible interlocked operations for PAL compatibility.

using Kernel.X64;

namespace Kernel.PAL;

/// <summary>
/// PAL Interlocked APIs - Win32-compatible atomic operations.
/// These are thin wrappers over Cpu atomic operations.
/// </summary>
public static unsafe class Interlocked
{
    /// <summary>
    /// Atomically increments a 32-bit value.
    /// Returns the incremented value (unlike Cpu.AtomicIncrement which returns original).
    /// </summary>
    public static int InterlockedIncrement(ref int target)
    {
        return Cpu.AtomicIncrement(ref target) + 1;
    }

    /// <summary>
    /// Atomically decrements a 32-bit value.
    /// Returns the decremented value (unlike Cpu.AtomicDecrement which returns original).
    /// </summary>
    public static int InterlockedDecrement(ref int target)
    {
        return Cpu.AtomicDecrement(ref target) - 1;
    }

    /// <summary>
    /// Atomically exchanges a 32-bit value.
    /// Returns the original value.
    /// </summary>
    public static int InterlockedExchange(ref int target, int value)
    {
        return Cpu.AtomicExchange(ref target, value);
    }

    /// <summary>
    /// Atomically compares and exchanges a 32-bit value.
    /// If target == comparand, sets target = value.
    /// Returns the original value of target.
    /// </summary>
    public static int InterlockedCompareExchange(ref int target, int value, int comparand)
    {
        return Cpu.AtomicCompareExchange(ref target, value, comparand);
    }

    /// <summary>
    /// Atomically adds to a 32-bit value.
    /// Returns the original value (before addition).
    /// </summary>
    public static int InterlockedExchangeAdd(ref int target, int value)
    {
        return Cpu.AtomicAdd(ref target, value);
    }

    /// <summary>
    /// Atomically increments a 64-bit value.
    /// Returns the incremented value.
    /// </summary>
    public static long InterlockedIncrement64(ref long target)
    {
        return Cpu.AtomicIncrement64(ref target) + 1;
    }

    /// <summary>
    /// Atomically decrements a 64-bit value.
    /// Returns the decremented value.
    /// </summary>
    public static long InterlockedDecrement64(ref long target)
    {
        return Cpu.AtomicDecrement64(ref target) - 1;
    }

    /// <summary>
    /// Atomically exchanges a 64-bit value.
    /// Returns the original value.
    /// </summary>
    public static long InterlockedExchange64(ref long target, long value)
    {
        return Cpu.AtomicExchange64(ref target, value);
    }

    /// <summary>
    /// Atomically compares and exchanges a 64-bit value.
    /// If target == comparand, sets target = value.
    /// Returns the original value of target.
    /// </summary>
    public static long InterlockedCompareExchange64(ref long target, long value, long comparand)
    {
        return Cpu.AtomicCompareExchange64(ref target, value, comparand);
    }

    /// <summary>
    /// Atomically adds to a 64-bit value.
    /// Returns the original value (before addition).
    /// </summary>
    public static long InterlockedExchangeAdd64(ref long target, long value)
    {
        return Cpu.AtomicAdd64(ref target, value);
    }

    /// <summary>
    /// Atomically exchanges a pointer value.
    /// Returns the original value.
    /// </summary>
    public static void* InterlockedExchangePointer(ref void* target, void* value)
    {
        return Cpu.AtomicExchangePointer(ref target, value);
    }

    /// <summary>
    /// Atomically compares and exchanges a pointer value.
    /// If target == comparand, sets target = value.
    /// Returns the original value of target.
    /// </summary>
    public static void* InterlockedCompareExchangePointer(ref void* target, void* value, void* comparand)
    {
        return Cpu.AtomicCompareExchangePointer(ref target, value, comparand);
    }

    /// <summary>
    /// Memory barrier - prevents reordering of memory operations across this point.
    /// </summary>
    public static void MemoryBarrier()
    {
        Cpu.MemoryBarrier();
    }
}
