// netos mernel - x64 Architecture Initialization
// Static initialization to avoid 'new' keyword issues in stdlib:zero

using System.Runtime.InteropServices;

namespace Mernel.X64;

/// <summary>
/// Static storage for interrupt handlers (fixed buffer wrapper)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HandlerStorage
{
    // 256 function pointers × 8 bytes = 2048 bytes
    public fixed byte Data[256 * 8];
}

/// <summary>
/// x64 architecture initialization.
/// Uses static methods instead of instance methods because stdlib:zero
/// doesn't support 'new' for reference types.
/// </summary>
public static unsafe class Arch
{
    private static bool _initialized;

    // Handler storage - function pointers for interrupt handlers (static, no heap allocation)
    private const int VectorCount = 256;
    private static HandlerStorage _handlerStorage;
    private static bool _interruptsEnabled;

    /// <summary>
    /// Initialize x64 architecture
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        DebugConsole.WriteLine("[x64] Initializing architecture...");

        // Initialize GDT
        Gdt.Init();

        // Clear handler storage (static storage, no heap allocation needed)
        fixed (byte* ptr = _handlerStorage.Data)
        {
            for (int i = 0; i < VectorCount * sizeof(void*); i++)
                ptr[i] = 0;
        }

        // Initialize IDT
        Idt.Init();

        // Initialize virtual memory (our own page tables)
        VirtualMemory.Init();

        _initialized = true;
        DebugConsole.WriteLine("[x64] Architecture initialized");
    }

    /// <summary>
    /// Register an interrupt handler
    /// </summary>
    public static void RegisterHandler(int vector, delegate*<InterruptFrame*, void> handler)
    {
        if (vector >= 0 && vector < VectorCount)
        {
            fixed (byte* ptr = _handlerStorage.Data)
            {
                var handlers = (delegate*<InterruptFrame*, void>*)ptr;
                handlers[vector] = handler;
            }
        }
    }

    /// <summary>
    /// Unregister an interrupt handler
    /// </summary>
    public static void UnregisterHandler(int vector)
    {
        if (vector >= 0 && vector < VectorCount)
        {
            fixed (byte* ptr = _handlerStorage.Data)
            {
                var handlers = (delegate*<InterruptFrame*, void>*)ptr;
                handlers[vector] = null;
            }
        }
    }

    /// <summary>
    /// Enable interrupts
    /// </summary>
    public static void EnableInterrupts()
    {
        _interruptsEnabled = true;
        Cpu.EnableInterrupts();
    }

    /// <summary>
    /// Disable interrupts
    /// </summary>
    public static void DisableInterrupts()
    {
        Cpu.DisableInterrupts();
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
        Cpu.Halt();
    }

    /// <summary>
    /// Trigger a breakpoint exception (for testing/debugging)
    /// </summary>
    public static void Breakpoint()
    {
        Cpu.Breakpoint();
    }

    /// <summary>
    /// Second-stage architecture initialization.
    /// Called after heap is ready, initializes timers and enables interrupts.
    /// </summary>
    public static void InitStage2()
    {
        DebugConsole.WriteLine("[x64] Stage 2 initialization...");

        // Initialize HPET (for calibration)
        if (!Hpet.Init())
        {
            DebugConsole.WriteLine("[x64] WARNING: HPET not available, timer calibration will be inaccurate");
        }

        // Initialize Local APIC
        if (Apic.Init())
        {
            // Calibrate timer using HPET if available
            if (Hpet.IsInitialized)
            {
                Apic.CalibrateTimer();
            }

            // Start periodic timer (10ms period = 100Hz)
            Apic.StartTimer(10);
        }

        // Enable interrupts
        EnableInterrupts();

        DebugConsole.WriteLine("[x64] Stage 2 complete, interrupts enabled");
    }

    /// <summary>
    /// Entry point from nernel ISR stubs
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "InterruptDispatch")]
    public static void DispatchInterrupt(InterruptFrame* frame)
    {
        int vector = (int)frame->InterruptNumber;

        fixed (byte* ptr = _handlerStorage.Data)
        {
            var handlers = (delegate*<InterruptFrame*, void>*)ptr;
            var handler = handlers[vector];
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
                DebugConsole.WriteHex(Cpu.ReadCr2());
                DebugConsole.WriteLine();
            }

            // Halt - can't continue from unhandled exception
            DebugConsole.WriteLine("!!! SYSTEM HALTED");
            Cpu.HaltForever();
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
