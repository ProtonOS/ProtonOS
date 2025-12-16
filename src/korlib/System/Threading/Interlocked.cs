// korlib - System.Threading.Interlocked
// Provides atomic operations for variables shared between threads.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace System.Threading;

/// <summary>
/// Provides atomic operations for variables that are shared by multiple threads.
/// </summary>
public static unsafe class Interlocked
{
#if KORLIB_IL
    // Stubs for IL build - actual implementation is in AOT kernel
    public static int Increment(ref int location) => ++location;
    public static long Increment(ref long location) => ++location;
    public static int Decrement(ref int location) => --location;
    public static long Decrement(ref long location) => --location;
    public static int Exchange(ref int location, int value) { var old = location; location = value; return old; }
    public static long Exchange(ref long location, long value) { var old = location; location = value; return old; }
    public static int CompareExchange(ref int location, int value, int comparand) { var old = location; if (old == comparand) location = value; return old; }
    public static long CompareExchange(ref long location, long value, long comparand) { var old = location; if (old == comparand) location = value; return old; }
    public static int Add(ref int location, int value) { location += value; return location; }
    public static long Add(ref long location, long value) { location += value; return location; }
    public static IntPtr Exchange(ref IntPtr location, IntPtr value) { var old = location; location = value; return old; }
    public static IntPtr CompareExchange(ref IntPtr location, IntPtr value, IntPtr comparand) { var old = location; if (old == comparand) location = value; return old; }
#else
    /// <summary>
    /// Increments a specified variable and stores the result, as an atomic operation.
    /// </summary>
    /// <param name="location">The variable whose value is to be incremented.</param>
    /// <returns>The incremented value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Increment(ref int location)
    {
        fixed (int* ptr = &location)
        {
            return Interlocked_Increment32(ptr);
        }
    }

    /// <summary>
    /// Increments a specified variable and stores the result, as an atomic operation.
    /// </summary>
    /// <param name="location">The variable whose value is to be incremented.</param>
    /// <returns>The incremented value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Increment(ref long location)
    {
        fixed (long* ptr = &location)
        {
            return Interlocked_Increment64(ptr);
        }
    }

    /// <summary>
    /// Decrements a specified variable and stores the result, as an atomic operation.
    /// </summary>
    /// <param name="location">The variable whose value is to be decremented.</param>
    /// <returns>The decremented value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Decrement(ref int location)
    {
        fixed (int* ptr = &location)
        {
            return Interlocked_Decrement32(ptr);
        }
    }

    /// <summary>
    /// Decrements a specified variable and stores the result, as an atomic operation.
    /// </summary>
    /// <param name="location">The variable whose value is to be decremented.</param>
    /// <returns>The decremented value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Decrement(ref long location)
    {
        fixed (long* ptr = &location)
        {
            return Interlocked_Decrement64(ptr);
        }
    }

    /// <summary>
    /// Sets a variable to a specified value and returns the original value, as an atomic operation.
    /// </summary>
    /// <param name="location">The variable to set to the specified value.</param>
    /// <param name="value">The value to which the location parameter is set.</param>
    /// <returns>The original value of location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Exchange(ref int location, int value)
    {
        fixed (int* ptr = &location)
        {
            return Interlocked_Exchange32(ptr, value);
        }
    }

    /// <summary>
    /// Sets a variable to a specified value and returns the original value, as an atomic operation.
    /// </summary>
    /// <param name="location">The variable to set to the specified value.</param>
    /// <param name="value">The value to which the location parameter is set.</param>
    /// <returns>The original value of location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Exchange(ref long location, long value)
    {
        fixed (long* ptr = &location)
        {
            return Interlocked_Exchange64(ptr, value);
        }
    }

    /// <summary>
    /// Compares two values for equality and, if they are equal, replaces the first value, as an atomic operation.
    /// </summary>
    /// <param name="location">The destination, whose value is compared with comparand and possibly replaced.</param>
    /// <param name="value">The value that replaces the destination value if the comparison results in equality.</param>
    /// <param name="comparand">The value that is compared to the value at location.</param>
    /// <returns>The original value in location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CompareExchange(ref int location, int value, int comparand)
    {
        fixed (int* ptr = &location)
        {
            return Interlocked_CompareExchange32(ptr, value, comparand);
        }
    }

    /// <summary>
    /// Compares two values for equality and, if they are equal, replaces the first value, as an atomic operation.
    /// </summary>
    /// <param name="location">The destination, whose value is compared with comparand and possibly replaced.</param>
    /// <param name="value">The value that replaces the destination value if the comparison results in equality.</param>
    /// <param name="comparand">The value that is compared to the value at location.</param>
    /// <returns>The original value in location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long CompareExchange(ref long location, long value, long comparand)
    {
        fixed (long* ptr = &location)
        {
            return Interlocked_CompareExchange64(ptr, value, comparand);
        }
    }

    /// <summary>
    /// Adds two integers and replaces the first integer with the sum, as an atomic operation.
    /// </summary>
    /// <param name="location">A variable containing the first value to be added. The sum is stored in location.</param>
    /// <param name="value">The value to be added to the integer at location.</param>
    /// <returns>The new value stored at location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Add(ref int location, int value)
    {
        fixed (int* ptr = &location)
        {
            return Interlocked_Add32(ptr, value);
        }
    }

    /// <summary>
    /// Adds two integers and replaces the first integer with the sum, as an atomic operation.
    /// </summary>
    /// <param name="location">A variable containing the first value to be added. The sum is stored in location.</param>
    /// <param name="value">The value to be added to the integer at location.</param>
    /// <returns>The new value stored at location.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Add(ref long location, long value)
    {
        fixed (long* ptr = &location)
        {
            return Interlocked_Add64(ptr, value);
        }
    }

    /// <summary>
    /// Sets a platform-specific handle or pointer to a specified value and returns the original value, as an atomic operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr Exchange(ref IntPtr location, IntPtr value)
    {
        fixed (IntPtr* ptr = &location)
        {
            return (IntPtr)Interlocked_ExchangePointer((void**)ptr, (void*)value);
        }
    }

    /// <summary>
    /// Compares two platform-specific handles or pointers for equality and, if they are equal, replaces the first one, as an atomic operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IntPtr CompareExchange(ref IntPtr location, IntPtr value, IntPtr comparand)
    {
        fixed (IntPtr* ptr = &location)
        {
            return (IntPtr)Interlocked_CompareExchangePointer((void**)ptr, (void*)value, (void*)comparand);
        }
    }

    // Kernel imports
    [DllImport("*", EntryPoint = "Interlocked_Increment32", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Interlocked_Increment32(int* location);

    [DllImport("*", EntryPoint = "Interlocked_Decrement32", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Interlocked_Decrement32(int* location);

    [DllImport("*", EntryPoint = "Interlocked_Exchange32", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Interlocked_Exchange32(int* location, int value);

    [DllImport("*", EntryPoint = "Interlocked_CompareExchange32", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Interlocked_CompareExchange32(int* location, int value, int comparand);

    [DllImport("*", EntryPoint = "Interlocked_Add32", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Interlocked_Add32(int* location, int value);

    [DllImport("*", EntryPoint = "Interlocked_Increment64", CallingConvention = CallingConvention.Cdecl)]
    private static extern long Interlocked_Increment64(long* location);

    [DllImport("*", EntryPoint = "Interlocked_Decrement64", CallingConvention = CallingConvention.Cdecl)]
    private static extern long Interlocked_Decrement64(long* location);

    [DllImport("*", EntryPoint = "Interlocked_Exchange64", CallingConvention = CallingConvention.Cdecl)]
    private static extern long Interlocked_Exchange64(long* location, long value);

    [DllImport("*", EntryPoint = "Interlocked_CompareExchange64", CallingConvention = CallingConvention.Cdecl)]
    private static extern long Interlocked_CompareExchange64(long* location, long value, long comparand);

    [DllImport("*", EntryPoint = "Interlocked_Add64", CallingConvention = CallingConvention.Cdecl)]
    private static extern long Interlocked_Add64(long* location, long value);

    [DllImport("*", EntryPoint = "Interlocked_ExchangePointer", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* Interlocked_ExchangePointer(void** location, void* value);

    [DllImport("*", EntryPoint = "Interlocked_CompareExchangePointer", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* Interlocked_CompareExchangePointer(void** location, void* value, void* comparand);
#endif
}
