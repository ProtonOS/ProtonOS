// ProtonOS kernel - x64 Architecture Initialization
// Static initialization to avoid 'new' keyword issues in stdlib:zero
// Implements IArchitecture for architecture-neutral kernel code.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;
using ProtonOS.X64;

namespace ProtonOS.X64;

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
/// Implements IArchitecture for architecture abstraction.
/// Note: This is a struct (not static class) to enable static abstract interface implementation,
/// but all members remain static. Use Arch.Method() syntax as before.
/// </summary>
public unsafe struct Arch : ProtonOS.Arch.IArchitecture<Arch>
{
    private static bool _stage1Complete;
    private static bool _stage2Complete;

    // Handler storage - function pointers for interrupt handlers (static, no heap allocation)
    private const int VectorCount = 256;
    private static HandlerStorage _handlerStorage;
    private static bool _interruptsEnabled;

    // ==================== IArchitecture Properties ====================

    /// <summary>
    /// Check if Stage 1 initialization is complete.
    /// </summary>
    public static bool IsStage1Complete => _stage1Complete;

    /// <summary>
    /// Check if Stage 2 initialization is complete.
    /// </summary>
    public static bool IsStage2Complete => _stage2Complete;

    /// <summary>
    /// Size of CPU context structure in bytes (CPUContext struct).
    /// </summary>
    public static int ContextSize => sizeof(CPUContext);

    /// <summary>
    /// Size of extended state (FPU/SSE/AVX) in bytes.
    /// Uses XSAVE area size if available, otherwise FXSAVE (512 bytes).
    /// </summary>
    public static int ExtendedStateSize => CPUFeatures.ExtendedStateSize > 0 ? (int)CPUFeatures.ExtendedStateSize : 512;

    // ==================== IArchitecture SMP Properties ====================

    /// <summary>
    /// Number of CPUs detected in the system.
    /// </summary>
    public static int CpuCount => CPUTopology.IsInitialized ? CPUTopology.CpuCount : 1;

    /// <summary>
    /// Index of the current CPU (0 to CpuCount-1).
    /// Uses per-CPU state via GS segment base.
    /// </summary>
    public static int CurrentCpuIndex => PerCpu.IsInitialized ? (int)PerCpu.CpuIndex : 0;

    /// <summary>
    /// Whether the current CPU is the Bootstrap Processor.
    /// </summary>
    public static bool IsBsp => !PerCpu.IsInitialized || PerCpu.IsBsp;

    // ==================== Initialization ====================

    /// <summary>
    /// Stage 1 initialization - Initialize x64 architecture (before heap)
    /// </summary>
    public static void InitStage1()
    {
        if (_stage1Complete) return;

        DebugConsole.WriteLine("[x64] Initializing architecture...");

        // Initialize GDT
        GDT.Init();

        // Clear handler storage (static storage, no heap allocation needed)
        fixed (byte* ptr = _handlerStorage.Data)
        {
            for (int i = 0; i < VectorCount * sizeof(void*); i++)
                ptr[i] = 0;
        }

        // Initialize IDT
        IDT.Init();

        // Initialize virtual memory (our own page tables)
        VirtualMemory.Init();

        _stage1Complete = true;
        DebugConsole.WriteLine("[x64] Architecture initialized");
    }

    /// <summary>
    /// Legacy alias for InitStage1() - maintained for backward compatibility.
    /// </summary>
    public static void Init() => InitStage1();

    /// <summary>
    /// Register an interrupt handler (x64-specific signature with InterruptFrame)
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
    /// Register an interrupt handler (IArchitecture interface - generic void* signature)
    /// </summary>
    public static void RegisterInterruptHandler(int vector, delegate*<void*, void> handler)
    {
        // Cast void* handler to InterruptFrame* handler (same memory layout)
        RegisterHandler(vector, (delegate*<InterruptFrame*, void>)handler);
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
    /// Unregister an interrupt handler (IArchitecture interface)
    /// </summary>
    public static void UnregisterInterruptHandler(int vector) => UnregisterHandler(vector);

    /// <summary>
    /// Enable interrupts
    /// </summary>
    public static void EnableInterrupts()
    {
        _interruptsEnabled = true;
        CPU.EnableInterrupts();
    }

    /// <summary>
    /// Disable interrupts
    /// </summary>
    public static void DisableInterrupts()
    {
        CPU.DisableInterrupts();
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
        CPU.Halt();
    }

    /// <summary>
    /// Trigger a breakpoint exception (for testing/debugging)
    /// </summary>
    public static void Breakpoint()
    {
        CPU.Breakpoint();
    }

    /// <summary>
    /// Second-stage architecture initialization.
    /// Called after heap is ready, initializes timers and enables interrupts.
    /// </summary>
    public static void InitStage2()
    {
        DebugConsole.WriteLine("[x64] Stage 2 initialization...");

        // Initialize CPU topology from MADT (requires heap, so done here not in Stage1)
        CPUTopology.Init();

        // Initialize exception handling
        ExceptionHandling.Init();

        // Initialize HPET (for calibration)
        if (!HPET.Init())
        {
            DebugConsole.WriteLine("[x64] WARNING: HPET not available, timer calibration will be inaccurate");
        }

        // Initialize RTC (for wall-clock time) - depends on HPET for elapsed time tracking
        RTC.Init();

        // Initialize Local APIC
        if (APIC.Init())
        {
            // Calibrate timer using HPET if available
            if (HPET.IsInitialized)
            {
                APIC.CalibrateTimer();
            }

            // Start periodic timer (1ms period = 1000Hz)
            APIC.StartTimer(1);
        }

        // Initialize I/O APIC for external interrupt routing
        if (CPUTopology.IOApicCount > 0)
        {
            if (IOAPIC.Init())
            {
                // Set up standard ISA IRQ routing to BSP
                IOAPIC.SetupIsaIrqs();
            }
        }

        // Start Application Processors (SMP)
        if (CPUTopology.CpuCount > 1)
        {
            SMP.Init();
            // Enable SMP mode in scheduler after APs are running
            Scheduler.EnableSmp();
        }

        // Enable interrupts
        EnableInterrupts();

        _stage2Complete = true;
        DebugConsole.WriteLine("[x64] Stage 2 complete, interrupts enabled");
    }

    // ==================== IArchitecture Timer Methods ====================

    /// <summary>
    /// Get the current timer tick count (from APIC timer).
    /// </summary>
    public static ulong GetTickCount()
    {
        return APIC.TickCount;
    }

    /// <summary>
    /// Get the timer frequency in Hz (APIC timer frequency).
    /// </summary>
    public static ulong GetTimerFrequency()
    {
        return APIC.TimerFrequency;
    }

    /// <summary>
    /// Busy-wait for the specified number of nanoseconds.
    /// </summary>
    public static void BusyWaitNs(ulong nanoseconds)
    {
        if (HPET.IsInitialized)
        {
            HPET.BusyWaitNs(nanoseconds);
        }
        else
        {
            // Fallback: rough spin loop (very inaccurate)
            ulong loops = nanoseconds / 10;  // Rough estimate
            for (ulong i = 0; i < loops; i++)
                CPU.Pause();
        }
    }

    /// <summary>
    /// Busy-wait for the specified number of milliseconds.
    /// </summary>
    public static void BusyWaitMs(ulong milliseconds)
    {
        BusyWaitNs(milliseconds * 1_000_000);
    }

    // ==================== IArchitecture Exception Handling ====================

    /// <summary>
    /// Get function pointer to the throw exception routine.
    /// </summary>
    public static delegate*<void*, void> GetThrowExceptionFuncPtr()
    {
        return CPU.GetThrowExFuncPtr();
    }

    /// <summary>
    /// Get function pointer to the rethrow routine.
    /// </summary>
    public static delegate*<void> GetRethrowFuncPtr()
    {
        return CPU.GetRethrowFuncPtr();
    }

    // ==================== Interrupt Dispatch ====================

    /// <summary>
    /// Entry point from kernel ISR stubs
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

        // CPU exceptions (0-31) - try SEH dispatch first
        if (vector < 32)
        {
            // Try to dispatch through exception handling infrastructure
            if (ExceptionHandling.DispatchException(frame, vector))
            {
                // Exception was handled, return to continue execution
                return;
            }

            // Unhandled exception - display info and halt
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
                DebugConsole.WriteHex(CPU.ReadCr2());
                DebugConsole.WriteLine();
            }

            // Halt - can't continue from unhandled exception
            DebugConsole.WriteLine("!!! SYSTEM HALTED");
            CPU.HaltForever();
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

    // ==================== IArchitecture SMP Methods ====================

    /// <summary>
    /// Initialize a secondary CPU after it has been started.
    /// Called by each AP after startup to complete initialization.
    /// </summary>
    /// <param name="cpuIndex">The CPU index being initialized</param>
    public static void InitSecondaryCpu(int cpuIndex)
    {
        // Called by SMP.ApEntry after AP completes trampoline
        // The per-CPU state and GS base are already set by SMP
        // Scheduler.InitSecondaryCpu creates the idle thread for this CPU
        Scheduler.InitSecondaryCpu();
    }

    /// <summary>
    /// Start all secondary CPUs.
    /// Uses INIT-SIPI-SIPI sequence via SMP.Init().
    /// </summary>
    public static void StartSecondaryCpus()
    {
        if (CPUTopology.CpuCount > 1)
        {
            SMP.Init();
            Scheduler.EnableSmp();
        }
    }

    /// <summary>
    /// Send an IPI (Inter-Processor Interrupt) to a specific CPU.
    /// </summary>
    /// <param name="targetCpu">CPU index to send to</param>
    /// <param name="vector">Interrupt vector number</param>
    public static void SendIpi(int targetCpu, int vector)
    {
        // Convert CPU index to APIC ID
        var cpuInfo = CPUTopology.GetCpu(targetCpu);
        if (cpuInfo != null)
        {
            APIC.SendIpi(cpuInfo->ApicId, (uint)vector);
        }
    }

    /// <summary>
    /// Broadcast an IPI to all CPUs except the current one.
    /// </summary>
    /// <param name="vector">Interrupt vector number</param>
    public static void BroadcastIpi(int vector)
    {
        APIC.BroadcastIpi((uint)vector);
    }
}
