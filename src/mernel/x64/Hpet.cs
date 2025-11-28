// netos mernel - HPET (High Precision Event Timer) driver
// Used as a reference clock for calibrating the Local APIC timer.
// HPET provides a known, stable frequency counter.

using System.Runtime.InteropServices;

namespace Mernel.X64;

/// <summary>
/// HPET memory-mapped register block.
/// Registers are at specific offsets, with reserved space between them.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HpetRegisters
{
    public ulong Capabilities;           // 0x000: General Capabilities and ID (RO)
    private ulong _reserved0;            // 0x008
    public ulong Configuration;          // 0x010: General Configuration (RW)
    private ulong _reserved1;            // 0x018
    public ulong InterruptStatus;        // 0x020: General Interrupt Status (RW1C)
    private fixed byte _reserved2[0x0F0 - 0x028];  // 0x028-0x0EF: Reserved (200 bytes)
    public ulong MainCounterValue;       // 0x0F0: Main Counter Value (RW)
}

/// <summary>
/// HPET (High Precision Event Timer) driver
/// </summary>
public static unsafe class Hpet
{
    // HPET Capability Register bits
    private const int CapCounterClockPeriodShift = 32;  // Bits 63:32 = period in femtoseconds

    // HPET Configuration Register bits
    private const ulong ConfigEnable = 1 << 0;          // Enable main counter
    private const ulong ConfigLegacyRoute = 1 << 1;     // Legacy replacement route

    private static HpetRegisters* _regs;
    private static ulong _frequencyHz;      // HPET frequency in Hz
    private static ulong _periodFs;         // Counter period in femtoseconds
    private static bool _initialized;

    /// <summary>
    /// Whether HPET is initialized and available
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// HPET counter frequency in Hz
    /// </summary>
    public static ulong FrequencyHz => _frequencyHz;

    /// <summary>
    /// Counter period in femtoseconds (10^-15 seconds)
    /// </summary>
    public static ulong PeriodFemtoseconds => _periodFs;

    /// <summary>
    /// Initialize HPET using ACPI HPET table.
    /// Must be called after Acpi.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        if (!Acpi.IsInitialized)
        {
            DebugConsole.WriteLine("[HPET] ACPI not initialized!");
            return false;
        }

        // Find HPET table
        var hpetTable = Acpi.FindHpet();
        if (hpetTable == null)
        {
            DebugConsole.WriteLine("[HPET] No HPET table found in ACPI");
            return false;
        }

        // Get base address
        if (hpetTable->BaseAddress.AddressSpaceId != 0)  // 0 = System Memory
        {
            DebugConsole.WriteLine("[HPET] HPET not memory-mapped!");
            return false;
        }

        _regs = (HpetRegisters*)hpetTable->BaseAddress.Address;

        DebugConsole.Write("[HPET] Base: 0x");
        DebugConsole.WriteHex((ulong)_regs);
        DebugConsole.WriteLine();

        // Read capabilities
        ulong caps = _regs->Capabilities;

        // Get counter period in femtoseconds (bits 63:32)
        _periodFs = caps >> CapCounterClockPeriodShift;
        if (_periodFs == 0)
        {
            DebugConsole.WriteLine("[HPET] Invalid counter period!");
            return false;
        }

        // Calculate frequency: 1e15 fs / period_fs = Hz
        // To avoid overflow: freq = 1,000,000,000,000,000 / period
        _frequencyHz = 1_000_000_000_000_000UL / _periodFs;

        DebugConsole.Write("[HPET] Period: ");
        DebugConsole.WriteHex(_periodFs);
        DebugConsole.Write(" fs, Frequency: ");
        DebugConsole.WriteHex(_frequencyHz);
        DebugConsole.WriteLine(" Hz");

        // Get number of timers (bits 12:8)
        int numTimers = (int)((caps >> 8) & 0x1F) + 1;
        DebugConsole.Write("[HPET] Number of timers: ");
        DebugConsole.WriteHex((ushort)numTimers);
        DebugConsole.WriteLine();

        // Check if 64-bit counter (bit 13)
        bool is64Bit = (caps & (1UL << 13)) != 0;
        DebugConsole.Write("[HPET] Counter width: ");
        DebugConsole.WriteLine(is64Bit ? "64-bit" : "32-bit");

        // Stop the counter before configuration
        _regs->Configuration &= ~ConfigEnable;

        // Reset main counter to 0
        _regs->MainCounterValue = 0;

        // Enable the main counter (but don't enable legacy routing)
        _regs->Configuration = ConfigEnable;

        _initialized = true;
        DebugConsole.WriteLine("[HPET] Initialized and running");

        return true;
    }

    /// <summary>
    /// Read the current HPET main counter value
    /// </summary>
    public static ulong ReadCounter()
    {
        if (!_initialized)
            return 0;
        return _regs->MainCounterValue;
    }

    /// <summary>
    /// Convert HPET ticks to nanoseconds
    /// </summary>
    public static ulong TicksToNanoseconds(ulong ticks)
    {
        // ns = ticks * period_fs / 1,000,000
        // To avoid overflow with large tick values, we use:
        // ns = ticks * (period_fs / 1000) / 1000
        return ticks * (_periodFs / 1000) / 1000;
    }

    /// <summary>
    /// Convert nanoseconds to HPET ticks
    /// </summary>
    public static ulong NanosecondsToTicks(ulong ns)
    {
        // ticks = ns * 1,000,000 / period_fs
        // To avoid overflow: ticks = ns * 1000 * 1000 / period_fs
        return ns * 1000 * 1000 / _periodFs;
    }

    /// <summary>
    /// Busy-wait for specified number of nanoseconds using HPET
    /// </summary>
    public static void BusyWaitNs(ulong nanoseconds)
    {
        if (!_initialized)
            return;

        ulong ticksToWait = NanosecondsToTicks(nanoseconds);
        ulong startTicks = ReadCounter();
        ulong endTicks = startTicks + ticksToWait;

        // Handle counter wrap-around (mainly for 32-bit counters)
        while (ReadCounter() < endTicks)
        {
            Cpu.Pause();  // Hint to CPU we're spinning
        }
    }

    /// <summary>
    /// Busy-wait for specified number of microseconds using HPET
    /// </summary>
    public static void BusyWaitUs(ulong microseconds)
    {
        BusyWaitNs(microseconds * 1000);
    }

    /// <summary>
    /// Busy-wait for specified number of milliseconds using HPET
    /// </summary>
    public static void BusyWaitMs(ulong milliseconds)
    {
        BusyWaitNs(milliseconds * 1_000_000);
    }
}
