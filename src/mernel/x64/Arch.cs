// netos mernel - x64 Architecture Initialization
// Static initialization to avoid 'new' keyword issues in stdlib:zero

using System.Runtime.InteropServices;

namespace Mernel.X64;

/// <summary>
/// x64 architecture initialization.
/// Uses static methods instead of instance methods because stdlib:zero
/// doesn't support 'new' for reference types.
/// </summary>
public static unsafe class Arch
{
    private static bool _initialized;

    // Handler storage - function pointers for interrupt handlers
    private const int VectorCount = 256;
    private static delegate*<InterruptFrame*, void>* _handlers;
    private static bool _interruptsEnabled;

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void cli();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sti();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void hlt();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong read_cr2();

    /// <summary>
    /// Initialize x64 architecture
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        DebugConsole.WriteLine("[x64] Initializing architecture...");

        // Initialize GDT
        Gdt.Init();

        // Allocate handler array
        _handlers = (delegate*<InterruptFrame*, void>*)NativeMemory.AllocZeroed(
            (nuint)(VectorCount * sizeof(void*)));

        // Initialize IDT
        Idt.Init();

        _initialized = true;
        DebugConsole.WriteLine("[x64] Architecture initialized");
    }

    /// <summary>
    /// Register an interrupt handler
    /// </summary>
    public static void RegisterHandler(int vector, delegate*<InterruptFrame*, void> handler)
    {
        if (vector >= 0 && vector < VectorCount && _handlers != null)
        {
            _handlers[vector] = handler;
        }
    }

    /// <summary>
    /// Unregister an interrupt handler
    /// </summary>
    public static void UnregisterHandler(int vector)
    {
        if (vector >= 0 && vector < VectorCount && _handlers != null)
        {
            _handlers[vector] = null;
        }
    }

    /// <summary>
    /// Enable interrupts
    /// </summary>
    public static void EnableInterrupts()
    {
        _interruptsEnabled = true;
        sti();
    }

    /// <summary>
    /// Disable interrupts
    /// </summary>
    public static void DisableInterrupts()
    {
        cli();
        _interruptsEnabled = false;
    }

    /// <summary>
    /// Check if interrupts are enabled
    /// </summary>
    public static bool InterruptsEnabled => _interruptsEnabled;

    /// <summary>
    /// Send End-Of-Interrupt signal
    /// </summary>
    public static void EndOfInterrupt(int vector)
    {
        // TODO: Send EOI to APIC/PIC when we implement them
    }

    /// <summary>
    /// Halt the CPU
    /// </summary>
    public static void Halt()
    {
        hlt();
    }

    /// <summary>
    /// Entry point from nernel ISR stubs
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "InterruptDispatch")]
    public static void DispatchInterrupt(InterruptFrame* frame)
    {
        int vector = (int)frame->InterruptNumber;

        if (_handlers != null)
        {
            var handler = _handlers[vector];
            if (handler != null)
            {
                handler(frame);
                return;
            }
        }

        // Default handling for unhandled interrupts
        DefaultHandler(frame);
    }

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
                DebugConsole.WriteHex(read_cr2());
                DebugConsole.WriteLine();
            }

            // Halt - can't continue from unhandled exception
            DebugConsole.WriteLine("!!! SYSTEM HALTED");
            cli();
            while (true)
            {
                hlt();
            }
        }

        // IRQs (32-47) - acknowledge and ignore if no handler
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
}
