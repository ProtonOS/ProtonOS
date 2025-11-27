// netos mernel - Interrupt handling
// Dispatch and default handlers for CPU exceptions and hardware interrupts.

using System.Runtime.InteropServices;

namespace Mernel;

/// <summary>
/// Interrupt dispatch and handling
/// </summary>
public static unsafe class Interrupts
{
    /// <summary>
    /// Main interrupt dispatcher called from nernel ISR stubs.
    /// This is the entry point from assembly.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "InterruptDispatch")]
    public static void Dispatch(InterruptFrame* frame)
    {
        int vector = (int)frame->InterruptNumber;

        // Try registered handler first
        var handler = Idt.GetHandler(vector);
        if (handler != null)
        {
            handler(frame);
            return;
        }

        // Default handling for unregistered interrupts
        DefaultHandler(frame);
    }

    /// <summary>
    /// Default handler for unhandled interrupts
    /// </summary>
    private static void DefaultHandler(InterruptFrame* frame)
    {
        int vector = (int)frame->InterruptNumber;

        // CPU exceptions (0-31) are fatal if unhandled
        if (vector < 32)
        {
            DebugConsole.WriteLine();
            DebugConsole.Write("!!! EXCEPTION ");
            DebugConsole.WriteHex((ushort)vector);
            DebugConsole.Write(": ");
            DebugConsole.WriteLine(GetExceptionName(vector));

            DebugConsole.Write("    Error Code: 0x");
            DebugConsole.WriteHex(frame->ErrorCode);
            DebugConsole.WriteLine();

            DebugConsole.Write("    RIP: 0x");
            DebugConsole.WriteHex(frame->Rip);
            DebugConsole.WriteLine();

            DebugConsole.Write("    RSP: 0x");
            DebugConsole.WriteHex(frame->Rsp);
            DebugConsole.WriteLine();

            DebugConsole.Write("    CS:  0x");
            DebugConsole.WriteHex((ushort)frame->Cs);
            DebugConsole.Write("  SS: 0x");
            DebugConsole.WriteHex((ushort)frame->Ss);
            DebugConsole.WriteLine();

            if (vector == 14) // Page fault
            {
                DebugConsole.Write("    CR2: 0x");
                DebugConsole.WriteHex(ReadCr2());
                DebugConsole.WriteLine();
            }

            // Halt - can't continue from unhandled exception
            DebugConsole.WriteLine("!!! SYSTEM HALTED");
            Halt();
        }

        // IRQs (32-47) - acknowledge and ignore if no handler
        // For now we don't have an interrupt controller set up
    }

    private static string GetExceptionName(int vector)
    {
        return vector switch
        {
            0 => "Divide by Zero",
            1 => "Debug",
            2 => "NMI",
            3 => "Breakpoint",
            4 => "Overflow",
            5 => "Bound Range Exceeded",
            6 => "Invalid Opcode",
            7 => "Device Not Available",
            8 => "Double Fault",
            9 => "Coprocessor Segment Overrun",
            10 => "Invalid TSS",
            11 => "Segment Not Present",
            12 => "Stack Segment Fault",
            13 => "General Protection Fault",
            14 => "Page Fault",
            16 => "x87 FPU Error",
            17 => "Alignment Check",
            18 => "Machine Check",
            19 => "SIMD Floating Point",
            20 => "Virtualization Exception",
            21 => "Control Protection",
            _ => "Unknown Exception",
        };
    }

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hlt();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cli();

    private static void Halt()
    {
        cli();
        while (true)
        {
            hlt();
        }
    }

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr2();

    private static ulong ReadCr2()
    {
        return read_cr2();
    }
}
