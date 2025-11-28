// netos mernel - Local APIC driver
// Provides Local APIC timer for preemptive scheduling.
// Timer is calibrated using HPET for accurate timing.

using System.Runtime.InteropServices;

namespace Mernel.X64;

/// <summary>
/// Local APIC Register offsets (memory-mapped at APIC base)
/// </summary>
public static class ApicRegisters
{
    public const uint Id = 0x020;                    // Local APIC ID
    public const uint Version = 0x030;              // Local APIC Version
    public const uint TaskPriority = 0x080;         // Task Priority Register
    public const uint Eoi = 0x0B0;                  // End of Interrupt
    public const uint SpuriousInterrupt = 0x0F0;    // Spurious Interrupt Vector
    public const uint ErrorStatus = 0x280;          // Error Status
    public const uint InterruptCommand = 0x300;     // Interrupt Command (low)
    public const uint InterruptCommandHigh = 0x310; // Interrupt Command (high)
    public const uint LvtTimer = 0x320;             // LVT Timer
    public const uint LvtThermal = 0x330;           // LVT Thermal Sensor
    public const uint LvtPerformance = 0x340;       // LVT Performance Counter
    public const uint LvtLint0 = 0x350;             // LVT LINT0
    public const uint LvtLint1 = 0x360;             // LVT LINT1
    public const uint LvtError = 0x370;             // LVT Error
    public const uint TimerInitialCount = 0x380;    // Timer Initial Count
    public const uint TimerCurrentCount = 0x390;    // Timer Current Count
    public const uint TimerDivideConfig = 0x3E0;    // Timer Divide Configuration
}

/// <summary>
/// Local APIC Timer modes
/// </summary>
public static class ApicTimerMode
{
    public const uint OneShot = 0 << 17;    // One-shot mode
    public const uint Periodic = 1 << 17;   // Periodic mode
    public const uint TscDeadline = 2 << 17; // TSC-Deadline mode
}

/// <summary>
/// Local APIC Timer divide values (for TimerDivideConfig register)
/// </summary>
public static class ApicTimerDivide
{
    public const uint By1 = 0b1011;   // Divide by 1
    public const uint By2 = 0b0000;   // Divide by 2
    public const uint By4 = 0b0001;   // Divide by 4
    public const uint By8 = 0b0010;   // Divide by 8
    public const uint By16 = 0b0011;  // Divide by 16
    public const uint By32 = 0b1000;  // Divide by 32
    public const uint By64 = 0b1001;  // Divide by 64
    public const uint By128 = 0b1010; // Divide by 128
}

/// <summary>
/// Local APIC LVT entry masks
/// </summary>
public static class ApicLvt
{
    public const uint VectorMask = 0xFF;          // Bits 0-7: interrupt vector
    public const uint DeliveryStatus = 1 << 12;   // Bit 12: delivery status (read-only)
    public const uint Masked = 1 << 16;           // Bit 16: interrupt masked
}

/// <summary>
/// APIC MSR addresses
/// </summary>
public static class ApicMsr
{
    public const uint ApicBase = 0x1B;  // IA32_APIC_BASE MSR
}

/// <summary>
/// Local APIC driver
/// </summary>
public static unsafe class Apic
{
    // Default Local APIC base address
    private const ulong DefaultApicBase = 0xFEE00000;

    // APIC timer interrupt vector
    public const int TimerVector = 32;  // First available vector after exceptions

    private static ulong _apicBase;
    private static ulong _ticksPerMs;           // APIC timer ticks per millisecond
    private static ulong _timerFrequency;       // APIC timer frequency in Hz
    private static bool _initialized;
    private static ulong _tickCount;            // Total timer ticks since init

    /// <summary>
    /// Whether the Local APIC is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// APIC timer frequency in Hz (after calibration)
    /// </summary>
    public static ulong TimerFrequency => _timerFrequency;

    /// <summary>
    /// Total tick count (incremented by timer interrupt handler)
    /// </summary>
    public static ulong TickCount => _tickCount;

    /// <summary>
    /// Read a Local APIC register
    /// </summary>
    private static uint ReadRegister(uint offset)
    {
        return *(uint*)(_apicBase + offset);
    }

    /// <summary>
    /// Write a Local APIC register
    /// </summary>
    private static void WriteRegister(uint offset, uint value)
    {
        *(uint*)(_apicBase + offset) = value;
    }

    /// <summary>
    /// Initialize the Local APIC.
    /// Must be called after VirtualMemory.Init() so we can use higher-half addresses.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        DebugConsole.WriteLine("[APIC] Initializing Local APIC...");

        // Read APIC base from MSR
        ulong apicBaseMsr = Cpu.ReadMsr(ApicMsr.ApicBase);
        _apicBase = apicBaseMsr & 0xFFFFF000;  // Bits 12-35 are the base address

        // Check if APIC is enabled globally (bit 11)
        bool globalEnabled = (apicBaseMsr & (1 << 11)) != 0;

        DebugConsole.Write("[APIC] Base: 0x");
        DebugConsole.WriteHex(_apicBase);
        DebugConsole.Write(" Global enable: ");
        DebugConsole.WriteLine(globalEnabled ? "yes" : "no");

        if (!globalEnabled)
        {
            // Enable APIC globally
            apicBaseMsr |= (1UL << 11);
            Cpu.WriteMsr(ApicMsr.ApicBase, apicBaseMsr);
            DebugConsole.WriteLine("[APIC] Enabled globally via MSR");
        }

        // Read APIC ID and version
        uint apicId = ReadRegister(ApicRegisters.Id) >> 24;
        uint version = ReadRegister(ApicRegisters.Version);
        uint maxLvtEntry = ((version >> 16) & 0xFF) + 1;

        DebugConsole.Write("[APIC] ID: ");
        DebugConsole.WriteHex((ushort)apicId);
        DebugConsole.Write(" Version: 0x");
        DebugConsole.WriteHex((ushort)(version & 0xFF));
        DebugConsole.Write(" Max LVT: ");
        DebugConsole.WriteHex((ushort)maxLvtEntry);
        DebugConsole.WriteLine();

        // Enable APIC via Spurious Interrupt Vector Register
        // Set spurious vector to 0xFF and set bit 8 (APIC software enable)
        WriteRegister(ApicRegisters.SpuriousInterrupt, 0xFF | (1 << 8));

        // Mask all LVT entries initially
        WriteRegister(ApicRegisters.LvtTimer, ApicLvt.Masked);
        WriteRegister(ApicRegisters.LvtLint0, ApicLvt.Masked);
        WriteRegister(ApicRegisters.LvtLint1, ApicLvt.Masked);
        WriteRegister(ApicRegisters.LvtError, ApicLvt.Masked);
        if (maxLvtEntry > 4)
            WriteRegister(ApicRegisters.LvtPerformance, ApicLvt.Masked);
        if (maxLvtEntry > 5)
            WriteRegister(ApicRegisters.LvtThermal, ApicLvt.Masked);

        // Clear any pending errors
        WriteRegister(ApicRegisters.ErrorStatus, 0);

        _initialized = true;
        DebugConsole.WriteLine("[APIC] Local APIC enabled");

        return true;
    }

    /// <summary>
    /// Calibrate APIC timer using HPET.
    /// Must be called after Hpet.Init().
    /// </summary>
    public static bool CalibrateTimer()
    {
        if (!_initialized)
        {
            DebugConsole.WriteLine("[APIC] Not initialized!");
            return false;
        }

        if (!Hpet.IsInitialized)
        {
            DebugConsole.WriteLine("[APIC] HPET not available for calibration!");
            return false;
        }

        DebugConsole.WriteLine("[APIC] Calibrating timer using HPET...");

        // Set timer divide to 1 for maximum resolution
        WriteRegister(ApicRegisters.TimerDivideConfig, ApicTimerDivide.By1);

        // Use a 10ms calibration period
        const ulong calibrationMs = 10;
        const ulong calibrationNs = calibrationMs * 1_000_000;

        // Start APIC timer with maximum initial count (one-shot mode)
        WriteRegister(ApicRegisters.LvtTimer, ApicLvt.Masked | ApicTimerMode.OneShot | TimerVector);
        WriteRegister(ApicRegisters.TimerInitialCount, 0xFFFFFFFF);

        // Wait using HPET
        ulong hpetStart = Hpet.ReadCounter();
        ulong hpetTicksToWait = Hpet.NanosecondsToTicks(calibrationNs);
        ulong hpetEnd = hpetStart + hpetTicksToWait;

        while (Hpet.ReadCounter() < hpetEnd)
        {
            Cpu.Pause();
        }

        // Read how many APIC ticks elapsed
        uint apicTicksElapsed = 0xFFFFFFFF - ReadRegister(ApicRegisters.TimerCurrentCount);

        // Stop the timer
        WriteRegister(ApicRegisters.TimerInitialCount, 0);

        // Calculate ticks per millisecond
        _ticksPerMs = apicTicksElapsed / calibrationMs;
        _timerFrequency = _ticksPerMs * 1000;

        DebugConsole.Write("[APIC] Timer frequency: ");
        DebugConsole.WriteHex(_timerFrequency);
        DebugConsole.Write(" Hz (");
        DebugConsole.WriteHex(_ticksPerMs);
        DebugConsole.WriteLine(" ticks/ms)");

        return true;
    }

    /// <summary>
    /// Start the APIC timer in periodic mode.
    /// </summary>
    /// <param name="periodMs">Timer period in milliseconds</param>
    public static void StartTimer(uint periodMs)
    {
        if (!_initialized || _ticksPerMs == 0)
            return;

        // Register timer interrupt handler
        Arch.RegisterHandler(TimerVector, &TimerInterruptHandler);

        // Calculate initial count for desired period
        uint initialCount = (uint)(_ticksPerMs * periodMs);

        DebugConsole.Write("[APIC] Starting timer, period ");
        DebugConsole.WriteHex((ushort)periodMs);
        DebugConsole.Write(" ms, initial count ");
        DebugConsole.WriteHex(initialCount);
        DebugConsole.WriteLine();

        // Configure timer: periodic mode, unmasked, vector 32
        WriteRegister(ApicRegisters.TimerDivideConfig, ApicTimerDivide.By1);
        WriteRegister(ApicRegisters.TimerInitialCount, initialCount);
        WriteRegister(ApicRegisters.LvtTimer, ApicTimerMode.Periodic | TimerVector);
    }

    /// <summary>
    /// Stop the APIC timer
    /// </summary>
    public static void StopTimer()
    {
        if (!_initialized)
            return;

        // Mask the timer and set initial count to 0
        WriteRegister(ApicRegisters.LvtTimer, ApicLvt.Masked);
        WriteRegister(ApicRegisters.TimerInitialCount, 0);
    }

    /// <summary>
    /// Send End-Of-Interrupt signal
    /// </summary>
    public static void SendEoi()
    {
        if (!_initialized)
            return;
        WriteRegister(ApicRegisters.Eoi, 0);
    }

    /// <summary>
    /// Timer interrupt handler
    /// </summary>
    private static void TimerInterruptHandler(InterruptFrame* frame)
    {
        _tickCount++;

        // Send EOI first to allow nested interrupts
        SendEoi();

        // Call scheduler timer tick for preemptive scheduling
        KernelScheduler.TimerTick();
    }
}
