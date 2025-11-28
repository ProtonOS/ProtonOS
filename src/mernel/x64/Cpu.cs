// netos mernel - x64 CPU intrinsics
// Centralized wrappers around native assembly for CPU-specific instructions.

using System.Runtime.InteropServices;

namespace Mernel.X64;

/// <summary>
/// CPU intrinsics for x64 - all native function imports in one place
/// </summary>
public static unsafe class Cpu
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
}
