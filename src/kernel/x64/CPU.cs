// ProtonOS kernel - x64 CPU intrinsics
// Centralized wrappers around native assembly for CPU-specific instructions.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;

namespace ProtonOS.X64;

/// <summary>
/// CPU intrinsics for x64 - all native function imports in one place.
/// Implements ICpu interface for architecture abstraction.
/// Note: This is a struct (not static class) to enable static abstract interface implementation,
/// but all members remain static. Use CPU.Method() syntax as before.
/// </summary>
public unsafe struct CPU : ProtonOS.Arch.ICpu<CPU>
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
    private static extern ulong read_cr0();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void write_cr0(ulong value);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr2();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr3();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void write_cr3(ulong value);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr4();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void write_cr4(ulong value);

    // I/O Port Access
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte inb(ushort port);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void outb(ushort port, byte value);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ushort inw(ushort port);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void outw(ushort port, ushort value);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint ind(ushort port);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void outd(ushort port, uint value);

    // CPUID
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cpuid_ex(uint leaf, uint subleaf, uint* eax, uint* ebx, uint* ecx, uint* edx);

    // XCR (Extended Control Registers)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong xgetbv(uint xcr);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xsetbv(uint xcr, ulong value);

    // FPU Initialization
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void fninit();

    // Extended State Save/Restore
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void fxsave(void* area);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void fxrstor(void* area);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xsave(void* area, ulong mask);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void xrstor(void* area, ulong mask);

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

    // TSC and Flags (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong rdtsc_native();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_flags();

    // ISR Table (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong* get_isr_table();

    // Context Switching (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void switch_context(CPUContext* oldContext, CPUContext* newContext);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void load_context(CPUContext* context);

    // Exception Throwing (from native.asm)
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void RhpThrowEx(void* exceptionObject);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void RhpRethrow();

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
    /// Read CR0
    /// </summary>
    public static ulong ReadCr0() => read_cr0();

    /// <summary>
    /// Write CR0
    /// </summary>
    public static void WriteCr0(ulong value) => write_cr0(value);

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

    /// <summary>
    /// Read CR4
    /// </summary>
    public static ulong ReadCr4() => read_cr4();

    /// <summary>
    /// Write CR4
    /// </summary>
    public static void WriteCr4(ulong value) => write_cr4(value);

    // --- I/O Port Access ---

    /// <summary>
    /// Read a byte from an I/O port
    /// </summary>
    public static byte InByte(ushort port) => inb(port);

    /// <summary>
    /// Write a byte to an I/O port
    /// </summary>
    public static void OutByte(ushort port, byte value) => outb(port, value);

    /// <summary>
    /// Read a word from an I/O port
    /// </summary>
    public static ushort InWord(ushort port) => inw(port);

    /// <summary>
    /// Write a word to an I/O port
    /// </summary>
    public static void OutWord(ushort port, ushort value) => outw(port, value);

    /// <summary>
    /// Read a dword from an I/O port
    /// </summary>
    public static uint InDword(ushort port) => ind(port);

    /// <summary>
    /// Write a dword to an I/O port
    /// </summary>
    public static void OutDword(ushort port, uint value) => outd(port, value);

    // --- CPUID ---

    /// <summary>
    /// Execute CPUID instruction with leaf and subleaf
    /// </summary>
    public static void Cpuid(uint leaf, uint subleaf, out uint eax, out uint ebx, out uint ecx, out uint edx)
    {
        uint a, b, c, d;
        cpuid_ex(leaf, subleaf, &a, &b, &c, &d);
        eax = a;
        ebx = b;
        ecx = c;
        edx = d;
    }

    /// <summary>
    /// Execute CPUID instruction with leaf only (subleaf = 0)
    /// </summary>
    public static void Cpuid(uint leaf, out uint eax, out uint ebx, out uint ecx, out uint edx)
        => Cpuid(leaf, 0, out eax, out ebx, out ecx, out edx);

    // --- XCR (Extended Control Registers) ---

    /// <summary>
    /// Read XCR0 (extended control register 0)
    /// </summary>
    public static ulong ReadXcr0() => xgetbv(0);

    /// <summary>
    /// Write XCR0 (extended control register 0)
    /// </summary>
    public static void WriteXcr0(ulong value) => xsetbv(0, value);

    // --- FPU ---

    /// <summary>
    /// Initialize x87 FPU
    /// </summary>
    public static void InitFpu() => fninit();

    // --- Extended State Save/Restore ---

    /// <summary>
    /// Save FPU/SSE state using FXSAVE (legacy, 512 bytes, 16-byte aligned)
    /// </summary>
    public static void Fxsave(void* area) => fxsave(area);

    /// <summary>
    /// Restore FPU/SSE state using FXRSTOR (legacy, 512 bytes, 16-byte aligned)
    /// </summary>
    public static void Fxrstor(void* area) => fxrstor(area);

    /// <summary>
    /// Save extended state using XSAVE (64-byte aligned, variable size)
    /// </summary>
    /// <param name="area">Pointer to 64-byte aligned XSAVE area</param>
    /// <param name="mask">State component mask (subset of XCR0)</param>
    public static void Xsave(void* area, ulong mask) => xsave(area, mask);

    /// <summary>
    /// Restore extended state using XRSTOR (64-byte aligned, variable size)
    /// </summary>
    /// <param name="area">Pointer to 64-byte aligned XSAVE area</param>
    /// <param name="mask">State component mask (subset of XCR0)</param>
    public static void Xrstor(void* area, ulong mask) => xrstor(area, mask);

    /// <summary>
    /// Save extended state using the best available method (XSAVE or FXSAVE)
    /// </summary>
    public static void SaveExtendedState(void* area)
    {
        if (CPUFeatures.UseXsave)
            xsave(area, 0xFFFFFFFFFFFFFFFF);  // Save all enabled components
        else
            fxsave(area);
    }

    /// <summary>
    /// Restore extended state using the best available method (XRSTOR or FXRSTOR)
    /// </summary>
    public static void RestoreExtendedState(void* area)
    {
        if (CPUFeatures.UseXsave)
            xrstor(area, 0xFFFFFFFFFFFFFFFF);  // Restore all enabled components
        else
            fxrstor(area);
    }

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

    // --- MSR Constants ---

    /// <summary>
    /// IA32_GS_BASE MSR - GS segment base address (used by kernel)
    /// </summary>
    public const uint MSR_GS_BASE = 0xC0000101;

    /// <summary>
    /// IA32_KERNEL_GS_BASE MSR - GS base swapped by SWAPGS (for user mode)
    /// </summary>
    public const uint MSR_KERNEL_GS_BASE = 0xC0000102;

    /// <summary>
    /// IA32_FS_BASE MSR - FS segment base address
    /// </summary>
    public const uint MSR_FS_BASE = 0xC0000100;

    // --- TSC (Time Stamp Counter) ---

    /// <summary>
    /// Read the Time Stamp Counter (RDTSC instruction).
    /// Returns the number of CPU cycles since reset.
    /// </summary>
    public static ulong ReadTsc() => rdtsc_native();

    // --- RFLAGS ---

    /// <summary>
    /// RFLAGS bit for Interrupt Flag (IF)
    /// </summary>
    public const ulong RFLAGS_IF = 1UL << 9;

    /// <summary>
    /// Read the RFLAGS register.
    /// </summary>
    public static ulong ReadFlags() => read_flags();

    /// <summary>
    /// Check if interrupts are currently enabled (IF flag set).
    /// </summary>
    public static bool AreInterruptsEnabled() => (read_flags() & RFLAGS_IF) != 0;

    // --- Per-CPU State Access (GS Base) ---

    /// <summary>
    /// Get the current GS segment base address.
    /// Used for per-CPU state access.
    /// </summary>
    public static ulong GetGsBase() => rdmsr(MSR_GS_BASE);

    /// <summary>
    /// Set the GS segment base address.
    /// Used during CPU initialization to point to per-CPU state.
    /// </summary>
    public static void SetGsBase(ulong value) => wrmsr(MSR_GS_BASE, value);

    /// <summary>
    /// Get the kernel GS base (used by SWAPGS).
    /// </summary>
    public static ulong GetKernelGsBase() => rdmsr(MSR_KERNEL_GS_BASE);

    /// <summary>
    /// Set the kernel GS base (used by SWAPGS).
    /// This is the value GS will be swapped to when entering kernel from user mode.
    /// </summary>
    public static void SetKernelGsBase(ulong value) => wrmsr(MSR_KERNEL_GS_BASE, value);

    /// <summary>
    /// Get the current FS segment base address.
    /// </summary>
    public static ulong GetFsBase() => rdmsr(MSR_FS_BASE);

    /// <summary>
    /// Set the FS segment base address.
    /// </summary>
    public static void SetFsBase(ulong value) => wrmsr(MSR_FS_BASE, value);

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

    // --- Exception Throwing ---

    /// <summary>
    /// Get the function pointer to RhpThrowEx for JIT code.
    /// </summary>
    public static delegate*<void*, void> GetThrowExFuncPtr()
        => &RhpThrowEx;

    /// <summary>
    /// Get the function pointer to RhpRethrow for JIT code.
    /// </summary>
    public static delegate*<void> GetRethrowFuncPtr()
        => &RhpRethrow;

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

    /// <summary>
    /// Get the current stack pointer value (alias for GetRsp).
    /// Implements ICpu interface method.
    /// </summary>
    public static ulong GetStackPointer() => get_rsp();

    // --- Per-CPU State Base (ICpu interface) ---

    /// <summary>
    /// Get the per-CPU state base address.
    /// Returns the GS base MSR value.
    /// </summary>
    public static ulong GetPerCpuStateBase() => GetGsBase();

    /// <summary>
    /// Set the per-CPU state base address.
    /// Sets the GS base MSR to the specified address.
    /// </summary>
    public static void SetPerCpuStateBase(ulong address) => SetGsBase(address);
}
