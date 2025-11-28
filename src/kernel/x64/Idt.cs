// netos mernel - x64 Interrupt Descriptor Table
// Sets up IDT entries pointing to ISR stubs in nernel.

using System.Runtime.InteropServices;
using Kernel.Platform;
using Kernel.Memory;
using Kernel.Threading;
using Kernel.X64;

namespace Kernel.X64;

/// <summary>
/// 16-byte IDT entry for 64-bit mode
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IdtEntry
{
    public ushort OffsetLow;     // Offset bits 0-15
    public ushort Selector;      // Code segment selector
    public byte Ist;             // Interrupt Stack Table (bits 0-2), rest zero
    public byte TypeAttr;        // Type and attributes
    public ushort OffsetMid;     // Offset bits 16-31
    public uint OffsetHigh;      // Offset bits 32-63
    public uint Reserved;        // Must be zero

    /// <summary>
    /// Create an interrupt gate entry
    /// </summary>
    public static IdtEntry InterruptGate(ulong handler, ushort selector, byte ist = 0)
    {
        return new IdtEntry
        {
            OffsetLow = (ushort)(handler & 0xFFFF),
            Selector = selector,
            Ist = (byte)(ist & 0x7),
            // Type: 0xE = 64-bit interrupt gate, Present=1, DPL=0
            // 1 00 0 1110 = 0x8E
            TypeAttr = 0x8E,
            OffsetMid = (ushort)((handler >> 16) & 0xFFFF),
            OffsetHigh = (uint)((handler >> 32) & 0xFFFFFFFF),
            Reserved = 0,
        };
    }

    /// <summary>
    /// Create a trap gate entry (doesn't disable interrupts)
    /// </summary>
    public static IdtEntry TrapGate(ulong handler, ushort selector, byte ist = 0)
    {
        return new IdtEntry
        {
            OffsetLow = (ushort)(handler & 0xFFFF),
            Selector = selector,
            Ist = (byte)(ist & 0x7),
            // Type: 0xF = 64-bit trap gate, Present=1, DPL=0
            // 1 00 0 1111 = 0x8F
            TypeAttr = 0x8F,
            OffsetMid = (ushort)((handler >> 16) & 0xFFFF),
            OffsetHigh = (uint)((handler >> 32) & 0xFFFFFFFF),
            Reserved = 0,
        };
    }
}

/// <summary>
/// IDT pointer structure for lidt instruction
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IdtPointer
{
    public ushort Limit;  // Size of IDT - 1
    public ulong Base;    // Linear address of IDT
}

/// <summary>
/// Interrupt frame passed to InterruptDispatch
/// Layout matches what ISR stubs push onto the stack.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InterruptFrame
{
    // Segment registers (pushed by isr_common)
    public ulong Es;
    public ulong Ds;

    // General purpose registers (pushed by isr_common)
    public ulong R15;
    public ulong R14;
    public ulong R13;
    public ulong R12;
    public ulong R11;
    public ulong R10;
    public ulong R9;
    public ulong R8;
    public ulong Rbp;
    public ulong Rdi;
    public ulong Rsi;
    public ulong Rdx;
    public ulong Rcx;
    public ulong Rbx;
    public ulong Rax;

    // Pushed by ISR stub
    public ulong InterruptNumber;
    public ulong ErrorCode;

    // Pushed by CPU on interrupt
    public ulong Rip;
    public ulong Cs;
    public ulong Rflags;
    public ulong Rsp;
    public ulong Ss;
}

/// <summary>
/// Static storage for IDT entries (fixed buffer wrapper)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct IdtStorage
{
    // 256 IDT entries × 16 bytes = 4096 bytes
    public fixed byte Data[256 * 16];
}

/// <summary>
/// Interrupt Descriptor Table management
/// </summary>
public static unsafe class Idt
{
    private const int IdtEntryCount = 256;

    // Static storage for IDT (fixed buffer, no heap allocation)
    private static IdtStorage _idtStorage;
    private static IdtPointer _idtPointer;

    /// <summary>
    /// Initialize and load the IDT
    /// </summary>
    public static void Init()
    {
        fixed (byte* idtPtr = _idtStorage.Data)
        {
            IdtEntry* idt = (IdtEntry*)idtPtr;

            // Set up IDT entries pointing to ISR stubs
            ulong* isrTable = Cpu.GetIsrTable();
            for (int i = 0; i < IdtEntryCount; i++)
            {
                ulong handler = isrTable[i];
                idt[i] = IdtEntry.InterruptGate(handler, GdtSelectors.KernelCode);
            }

            // Set up IDT pointer
            _idtPointer.Limit = (ushort)(IdtEntryCount * sizeof(IdtEntry) - 1);
            _idtPointer.Base = (ulong)idt;

            // Load the IDT
            fixed (IdtPointer* ptr = &_idtPointer)
            {
                Cpu.LoadIdt(ptr);
            }

            DebugConsole.Write("[IDT] Loaded at 0x");
            DebugConsole.WriteHex((ulong)idt);
            DebugConsole.WriteLine();
        }
    }
}
