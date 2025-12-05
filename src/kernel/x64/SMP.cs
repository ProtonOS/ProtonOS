// ProtonOS kernel - SMP (Symmetric Multi-Processing) Support
// Handles Application Processor startup and per-CPU state initialization.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;

namespace ProtonOS.X64;

/// <summary>
/// AP startup data structure - must match native.asm ap_startup_data layout
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ApStartupData
{
    public ulong Cr3;           // Page table base (offset 0)
    public ulong GdtPtr;        // Pointer to GDT descriptor (offset 8)
    public ulong Stack;         // Stack pointer for AP (offset 16)
    public ulong PerCpu;        // Per-CPU state pointer (offset 24)
    public ulong Entry;         // C# ApEntry function pointer (offset 32)
    public uint ApRunning;      // Set to 1 when AP is running (offset 40)
    public uint ApId;           // Target APIC ID (offset 44)
    public ulong IdtPtr;        // Pointer to IDT descriptor (offset 48)
}

/// <summary>
/// SMP initialization and AP management
/// </summary>
public static unsafe class SMP
{
    // AP trampoline is copied to this physical address (below 1MB, 4KB aligned)
    // IMPORTANT: Must not conflict with page allocator! Page allocator starts at ~0x1000
    // and works upward through 0xC000+ for page tables. We use 0x90000 (576KB) which is
    // high enough to avoid page table allocations but still below 1MB for SIPI.
    private const ulong TrampolineAddress = 0x90000;

    // Size of per-AP kernel stack
    private const ulong ApStackSize = 16384; // 16KB per AP

    // Native assembly symbols
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_ap_trampoline_start();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_ap_trampoline_size();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ApStartupData* get_ap_startup_data();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* memcpy(void* dest, void* src, ulong count);

    private static PerCpuState** _perCpuStates;
    private static int _cpuCount;
    private static int _onlineCount;
    private static bool _initialized;
    private static SpinLock _initLock;

    /// <summary>
    /// Whether SMP initialization has completed
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Number of online CPUs
    /// </summary>
    public static int OnlineCount => _onlineCount;

    /// <summary>
    /// Initialize SMP - start all application processors
    /// Must be called after CPUTopology.Init(), APIC.Init(), and heap is available.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        _cpuCount = CPUTopology.CpuCount;
        if (_cpuCount <= 1)
        {
            DebugConsole.WriteLine("[SMP] Single CPU system, skipping AP startup");
            SetupBspPerCpuState();
            _onlineCount = 1;
            _initialized = true;
            return;
        }

        DebugConsole.Write("[SMP] Starting ");
        DebugConsole.WriteDecimal(_cpuCount - 1);
        DebugConsole.WriteLine(" application processor(s)");

        // Initialize per-CPU state tracking
        PerCpu.Init(_cpuCount);

        // Allocate per-CPU state structures
        AllocatePerCpuStates();

        // Set up BSP's per-CPU state
        SetupBspPerCpuState();
        _onlineCount = 1;

        // Copy AP trampoline to low memory
        CopyTrampoline();

        // Start all APs
        StartApplicationProcessors();

        _initialized = true;

        DebugConsole.Write("[SMP] ");
        DebugConsole.WriteDecimal(_onlineCount);
        DebugConsole.Write(" of ");
        DebugConsole.WriteDecimal(_cpuCount);
        DebugConsole.WriteLine(" CPUs online");
    }

    /// <summary>
    /// Allocate per-CPU state structures for all CPUs
    /// </summary>
    private static void AllocatePerCpuStates()
    {
        _perCpuStates = (PerCpuState**)HeapAllocator.AllocZeroed((ulong)(sizeof(PerCpuState*) * _cpuCount));

        for (int i = 0; i < _cpuCount; i++)
        {
            // Allocate per-CPU state (should be cache-line aligned in production)
            var state = (PerCpuState*)HeapAllocator.AllocZeroed((ulong)sizeof(PerCpuState));

            // Set self-pointer (critical for GS:0 access pattern)
            state->Self = state;

            // Get CPU info from topology
            var cpuInfo = CPUTopology.GetCpu(i);
            if (cpuInfo != null)
            {
                state->CpuIndex = cpuInfo->CpuIndex;
                state->ApicId = cpuInfo->ApicId;
                state->NumaNode = cpuInfo->NumaNode;
                state->IsBsp = cpuInfo->IsBsp;
            }

            _perCpuStates[i] = state;

            // Register with PerCpu static class
            PerCpu.RegisterCpu(i, state);
        }
    }

    /// <summary>
    /// Set up the BSP's per-CPU state
    /// </summary>
    private static void SetupBspPerCpuState()
    {
        int bspIndex = CPUTopology.BspIndex;
        if (bspIndex < 0 || bspIndex >= _cpuCount)
        {
            DebugConsole.WriteLine("[SMP] ERROR: Invalid BSP index");
            return;
        }

        var bspState = _perCpuStates[bspIndex];

        // Set GS base to point to BSP's per-CPU state
        CPU.SetGsBase((ulong)bspState);

        DebugConsole.Write("[SMP] BSP per-CPU state at 0x");
        DebugConsole.WriteHex((ulong)bspState);
        DebugConsole.Write(" CPU ");
        DebugConsole.WriteDecimal((int)bspState->CpuIndex);
        DebugConsole.Write(" APIC ");
        DebugConsole.WriteDecimal((int)bspState->ApicId);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Copy the AP trampoline code to low memory
    /// </summary>
    private static void CopyTrampoline()
    {
        ulong trampolineStart = get_ap_trampoline_start();
        ulong trampolineSize = get_ap_trampoline_size();

        DebugConsole.Write("[SMP] Copying trampoline (");
        DebugConsole.WriteDecimal((int)trampolineSize);
        DebugConsole.Write(" bytes) to 0x");
        DebugConsole.WriteHex(TrampolineAddress);
        DebugConsole.WriteLine();

        // Copy trampoline to low memory
        // In UEFI, physical addresses below 1MB should be accessible
        memcpy((void*)TrampolineAddress, (void*)trampolineStart, trampolineSize);
    }

    /// <summary>
    /// Start all application processors
    /// </summary>
    private static void StartApplicationProcessors()
    {
        for (int i = 0; i < _cpuCount; i++)
        {
            var cpuInfo = CPUTopology.GetCpu(i);
            if (cpuInfo == null || cpuInfo->IsBsp || !cpuInfo->IsEnabled)
                continue;

            StartAp(i, cpuInfo);
        }
    }

    /// <summary>
    /// Start a single application processor
    /// </summary>
    private static void StartAp(int cpuIndex, CpuInfo* cpuInfo)
    {
        DebugConsole.Write("[SMP] Starting CPU ");
        DebugConsole.WriteDecimal(cpuIndex);
        DebugConsole.Write(" (APIC ");
        DebugConsole.WriteDecimal((int)cpuInfo->ApicId);
        DebugConsole.Write(")... ");

        // Get per-CPU state for this AP
        var perCpu = _perCpuStates[cpuIndex];

        // Allocate stack for this AP
        ulong stackBase = (ulong)HeapAllocator.AllocZeroed(ApStackSize);
        ulong stackTop = stackBase + ApStackSize;

        // Set up AP startup data
        var startupData = get_ap_startup_data();
        startupData->Cr3 = CPU.ReadCr3();
        startupData->GdtPtr = (ulong)GDT.GetGdtPointer();
        startupData->Stack = stackTop;
        startupData->PerCpu = (ulong)perCpu;
        startupData->Entry = (ulong)(delegate* unmanaged<PerCpuState*, void>)&ApEntry;
        startupData->ApRunning = 0;
        startupData->ApId = cpuInfo->ApicId;
        startupData->IdtPtr = (ulong)IDT.GetIdtPointer();

        // Memory barrier to ensure all writes are visible
        CPU.MemoryBarrier();

        // Send INIT IPI
        APIC.SendInitIpi(cpuInfo->ApicId);

        // Wait 10ms for AP to process INIT
        BusyWait(10000);

        // Send SIPI (Startup IPI) with trampoline page number
        // Page number = physical address >> 12
        byte trampolineVector = (byte)(TrampolineAddress >> 12);
        APIC.SendStartupIpi(cpuInfo->ApicId, trampolineVector);

        // Wait 200us
        BusyWait(200);

        // Send second SIPI (required by Intel spec)
        APIC.SendStartupIpi(cpuInfo->ApicId, trampolineVector);

        // Wait for AP to signal it's running (timeout after 100ms)
        int timeout = 100000; // microseconds
        uint* runningPtr = &startupData->ApRunning;
        while (*runningPtr == 0 && timeout > 0)
        {
            BusyWait(100);
            timeout -= 100;
        }

        // Read final value
        uint finalValue = *runningPtr;

        if (finalValue != 0)
        {
            DebugConsole.WriteLine("OK");
            _initLock.Acquire();
            _onlineCount++;
            _initLock.Release();
            CPUTopology.SetCpuOnline(cpuInfo->ApicId, true);
        }
        else
        {
            DebugConsole.WriteLine("TIMEOUT");
        }
    }

    /// <summary>
    /// Busy wait for specified microseconds
    /// Uses a simple loop - not accurate but good enough for AP startup
    /// </summary>
    private static void BusyWait(int microseconds)
    {
        // Rough approximation: assume ~1000 iterations per microsecond on modern CPU
        // This is very imprecise but acceptable for AP startup delays
        for (int i = 0; i < microseconds * 100; i++)
        {
            CPU.Pause();
        }
    }

    /// <summary>
    /// AP entry point - called by trampoline after mode transitions
    /// </summary>
    [UnmanagedCallersOnly]
    public static void ApEntry(PerCpuState* perCpu)
    {
        // Set GS base to our per-CPU state
        CPU.SetGsBase((ulong)perCpu);

        // Initialize local APIC for this CPU
        APIC.InitAp();

        // Initialize scheduler state for this AP (creates idle thread)
        Scheduler.InitSecondaryCpu();

        // Signal that we're running
        var startupData = get_ap_startup_data();
        startupData->ApRunning = 1;

        // Enable interrupts on this AP
        CPU.EnableInterrupts();

        // Enter idle loop until scheduler gives us work
        ApIdleLoop();
    }

    /// <summary>
    /// AP idle loop - runs when no threads are ready
    /// </summary>
    private static void ApIdleLoop()
    {
        while (true)
        {
            // Halt until interrupt (timer will wake us)
            CPU.Halt();

            // Check if scheduler has work for us
            // Once scheduler is updated for SMP, it will handle this
        }
    }
}
