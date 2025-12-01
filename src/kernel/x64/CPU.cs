// ProtonOS kernel - x64 CPU intrinsics
// Centralized wrappers around native assembly for CPU-specific instructions.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;

namespace ProtonOS.X64;

/// <summary>
/// CPU intrinsics for x64 - all native function imports in one place
/// </summary>
public static unsafe class CPU
{
    // ==================== Native Imports ====================

    // CPU Control
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cli();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sti();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hlt();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void pause();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void int3();

    // Control Registers
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr2();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr3();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void write_cr3(ulong value);

    // Descriptor Tables
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void lgdt(void* gdtPtr);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void lidt(void* idtPtr);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ltr(ushort selector);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void reload_segments(ushort codeSelector, ushort dataSelector);

    // TLB
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void invlpg(ulong virtualAddress);

    // MSR (Model Specific Registers)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong rdmsr(uint msr);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void wrmsr(uint msr, ulong value);

    // ISR Table (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong* get_isr_table();

    // Context Switching (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void switch_context(CPUContext* oldContext, CPUContext* newContext);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void load_context(CPUContext* context);

    // PAL Context Restore (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void restore_pal_context(void* context);

    // Memory Barrier (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void mfence();

    // Memory Operations
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* memcpy(void* dest, void* src, ulong count);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* memset(void* dest, int c, ulong count);

    // Register Access
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_rsp();

    // Atomic Operations - 32-bit (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern int atomic_cmpxchg32(int* ptr, int newVal, int comparand);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern int atomic_xchg32(int* ptr, int newVal);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern int atomic_add32(int* ptr, int addend);

    // Atomic Operations - 64-bit (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern long atomic_cmpxchg64(long* ptr, long newVal, long comparand);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern long atomic_xchg64(long* ptr, long newVal);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern long atomic_add64(long* ptr, long addend);

    // ==================== Public API ====================

    // --- Interrupt Control ---

    /// <summary>
    /// Disable interrupts (clear interrupt flag)
    /// </summary>
    public static void DisableInterrupts() => cli();

    /// <summary>
    /// Enable interrupts (set interrupt flag)
    /// </summary>
    public static void EnableInterrupts() => sti();

    /// <summary>
    /// Halt the CPU until next interrupt
    /// </summary>
    public static void Halt() => hlt();

    /// <summary>
    /// Spin-wait hint for busy loops
    /// </summary>
    public static void Pause() => pause();

    /// <summary>
    /// Trigger a breakpoint exception (INT3)
    /// </summary>
    public static void Breakpoint() => int3();

    /// <summary>
    /// Halt forever (disable interrupts and halt in loop)
    /// </summary>
    public static void HaltForever()
    {
        cli();
        while (true)
            hlt();
    }

    // --- Control Registers ---

    /// <summary>
    /// Read CR2 (page fault linear address)
    /// </summary>
    public static ulong ReadCr2() => read_cr2();

    /// <summary>
    /// Read CR3 (page table base address)
    /// </summary>
    public static ulong ReadCr3() => read_cr3();

    /// <summary>
    /// Write CR3 (switch page tables)
    /// </summary>
    public static void WriteCr3(ulong pml4PhysAddr) => write_cr3(pml4PhysAddr);

    // --- Descriptor Tables ---

    /// <summary>
    /// Load Global Descriptor Table
    /// </summary>
    public static void LoadGdt(void* gdtPtr) => lgdt(gdtPtr);

    /// <summary>
    /// Load Interrupt Descriptor Table
    /// </summary>
    public static void LoadIdt(void* idtPtr) => lidt(idtPtr);

    /// <summary>
    /// Load Task Register
    /// </summary>
    public static void LoadTr(ushort selector) => ltr(selector);

    /// <summary>
    /// Reload segment registers after GDT change
    /// </summary>
    public static void ReloadSegments(ushort codeSelector, ushort dataSelector)
        => reload_segments(codeSelector, dataSelector);

    // --- TLB ---

    /// <summary>
    /// Invalidate a single TLB entry
    /// </summary>
    public static void Invlpg(ulong virtualAddress) => invlpg(virtualAddress);

    /// <summary>
    /// Flush entire TLB by reloading CR3
    /// </summary>
    public static void FlushTlb()
    {
        write_cr3(read_cr3());
    }

    // --- ISR Table ---

    /// <summary>
    /// Get pointer to ISR stub table (from native.asm)
    /// </summary>
    public static ulong* GetIsrTable() => get_isr_table();

    // --- MSR (Model Specific Registers) ---

    /// <summary>
    /// Read a Model Specific Register
    /// </summary>
    public static ulong ReadMsr(uint msr) => rdmsr(msr);

    /// <summary>
    /// Write a Model Specific Register
    /// </summary>
    public static void WriteMsr(uint msr, ulong value) => wrmsr(msr, value);

    // --- Context Switching ---

    /// <summary>
    /// Switch from current context to new context.
    /// Saves current CPU state to oldContext, loads newContext.
    /// </summary>
    public static void SwitchContext(CPUContext* oldContext, CPUContext* newContext)
        => switch_context(oldContext, newContext);

    /// <summary>
    /// Load a context without saving current state.
    /// Used for initial thread switch.
    /// </summary>
    public static void LoadContext(CPUContext* context)
        => load_context(context);

    /// <summary>
    /// Restore a PAL CONTEXT structure.
    /// This function does not return - execution continues at Context.Rip.
    /// Used for RtlRestoreContext and exception unwinding.
    /// </summary>
    public static void RestorePALContext(void* context)
        => restore_pal_context(context);

    // --- Atomic Operations ---

    /// <summary>
    /// Atomic compare-and-exchange for 32-bit integers.
    /// If *location == comparand, sets *location = value.
    /// Returns the original value at location.
    /// </summary>
    public static int AtomicCompareExchange(ref int location, int value, int comparand)
    {
        fixed (int* ptr = &location)
        {
            return atomic_cmpxchg32(ptr, value, comparand);
        }
    }

    /// <summary>
    /// Atomic exchange for 32-bit integers.
    /// Sets *location = value and returns the original value.
    /// </summary>
    public static int AtomicExchange(ref int location, int value)
    {
        fixed (int* ptr = &location)
        {
            return atomic_xchg32(ptr, value);
        }
    }

    /// <summary>
    /// Atomic increment for 32-bit integers.
    /// Returns the original value (before increment).
    /// </summary>
    public static int AtomicIncrement(ref int location)
    {
        fixed (int* ptr = &location)
        {
            return atomic_add32(ptr, 1);
        }
    }

    /// <summary>
    /// Atomic decrement for 32-bit integers.
    /// Returns the original value (before decrement).
    /// </summary>
    public static int AtomicDecrement(ref int location)
    {
        fixed (int* ptr = &location)
        {
            return atomic_add32(ptr, -1);
        }
    }

    /// <summary>
    /// Atomic add for 32-bit integers.
    /// Returns the original value (before addition).
    /// </summary>
    public static int AtomicAdd(ref int location, int addend)
    {
        fixed (int* ptr = &location)
        {
            return atomic_add32(ptr, addend);
        }
    }

    // --- 64-bit Atomic Operations ---

    /// <summary>
    /// Atomic compare-and-exchange for 64-bit integers.
    /// If *location == comparand, sets *location = value.
    /// Returns the original value at location.
    /// </summary>
    public static long AtomicCompareExchange64(ref long location, long value, long comparand)
    {
        fixed (long* ptr = &location)
        {
            return atomic_cmpxchg64(ptr, value, comparand);
        }
    }

    /// <summary>
    /// Atomic exchange for 64-bit integers.
    /// Sets *location = value and returns the original value.
    /// </summary>
    public static long AtomicExchange64(ref long location, long value)
    {
        fixed (long* ptr = &location)
        {
            return atomic_xchg64(ptr, value);
        }
    }

    /// <summary>
    /// Atomic increment for 64-bit integers.
    /// Returns the original value (before increment).
    /// </summary>
    public static long AtomicIncrement64(ref long location)
    {
        fixed (long* ptr = &location)
        {
            return atomic_add64(ptr, 1);
        }
    }

    /// <summary>
    /// Atomic decrement for 64-bit integers.
    /// Returns the original value (before decrement).
    /// </summary>
    public static long AtomicDecrement64(ref long location)
    {
        fixed (long* ptr = &location)
        {
            return atomic_add64(ptr, -1);
        }
    }

    /// <summary>
    /// Atomic add for 64-bit integers.
    /// Returns the original value (before addition).
    /// </summary>
    public static long AtomicAdd64(ref long location, long addend)
    {
        fixed (long* ptr = &location)
        {
            return atomic_add64(ptr, addend);
        }
    }

    /// <summary>
    /// Atomic compare-and-exchange for pointers.
    /// If *location == comparand, sets *location = value.
    /// Returns the original value at location.
    /// </summary>
    public static void* AtomicCompareExchangePointer(ref void* location, void* value, void* comparand)
    {
        fixed (void** ptr = &location)
        {
            return (void*)atomic_cmpxchg64((long*)ptr, (long)value, (long)comparand);
        }
    }

    /// <summary>
    /// Atomic exchange for pointers.
    /// Sets *location = value and returns the original value.
    /// </summary>
    public static void* AtomicExchangePointer(ref void* location, void* value)
    {
        fixed (void** ptr = &location)
        {
            return (void*)atomic_xchg64((long*)ptr, (long)value);
        }
    }

    // --- Memory Barriers ---

    /// <summary>
    /// Full memory barrier (prevents all reordering across the barrier)
    /// </summary>
    public static void MemoryBarrier() => mfence();

    // --- Memory Operations ---

    /// <summary>
    /// Copy memory from source to destination using optimized rep movsb.
    /// </summary>
    /// <param name="dest">Destination pointer</param>
    /// <param name="src">Source pointer</param>
    /// <param name="count">Number of bytes to copy</param>
    /// <returns>Destination pointer</returns>
    public static void* MemCopy(void* dest, void* src, ulong count) => memcpy(dest, src, count);

    /// <summary>
    /// Fill memory with a byte value using optimized rep stosb.
    /// </summary>
    /// <param name="dest">Destination pointer</param>
    /// <param name="value">Byte value to fill with</param>
    /// <param name="count">Number of bytes to fill</param>
    /// <returns>Destination pointer</returns>
    public static void* MemSet(void* dest, byte value, ulong count) => memset(dest, value, count);

    /// <summary>
    /// Zero memory using optimized rep stosb.
    /// </summary>
    /// <param name="dest">Destination pointer</param>
    /// <param name="count">Number of bytes to zero</param>
    /// <returns>Destination pointer</returns>
    public static void* MemZero(void* dest, ulong count) => memset(dest, 0, count);

    // --- Register Access ---

    /// <summary>
    /// Get the current RSP (stack pointer) value.
    /// Used for stack bounds detection.
    /// </summary>
    public static ulong GetRsp() => get_rsp();
}
