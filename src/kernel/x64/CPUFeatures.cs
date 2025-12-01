// ProtonOS kernel - CPU Feature Detection and Initialization
// Detects and enables CPU features required for JIT code execution.

using ProtonOS.Platform;

namespace ProtonOS.X64;

/// <summary>
/// CR0 register bit definitions
/// </summary>
public static class CR0
{
    public const ulong PE = 1UL << 0;   // Protected Mode Enable
    public const ulong MP = 1UL << 1;   // Monitor Coprocessor
    public const ulong EM = 1UL << 2;   // Emulation (must be 0 for FPU/SSE)
    public const ulong TS = 1UL << 3;   // Task Switched (triggers #NM for lazy FPU save)
    public const ulong ET = 1UL << 4;   // Extension Type (read-only, 1 = 387 compatible)
    public const ulong NE = 1UL << 5;   // Numeric Error (native FPU exceptions)
    public const ulong WP = 1UL << 16;  // Write Protect (supervisor write protection)
    public const ulong AM = 1UL << 18;  // Alignment Mask
    public const ulong NW = 1UL << 29;  // Not Write-through
    public const ulong CD = 1UL << 30;  // Cache Disable
    public const ulong PG = 1UL << 31;  // Paging Enable
}

/// <summary>
/// CR4 register bit definitions
/// </summary>
public static class CR4
{
    public const ulong VME = 1UL << 0;          // Virtual-8086 Mode Extensions
    public const ulong PVI = 1UL << 1;          // Protected-Mode Virtual Interrupts
    public const ulong TSD = 1UL << 2;          // Time Stamp Disable
    public const ulong DE = 1UL << 3;           // Debugging Extensions
    public const ulong PSE = 1UL << 4;          // Page Size Extension
    public const ulong PAE = 1UL << 5;          // Physical Address Extension
    public const ulong MCE = 1UL << 6;          // Machine Check Exception
    public const ulong PGE = 1UL << 7;          // Page Global Enable
    public const ulong PCE = 1UL << 8;          // Performance Monitoring Counter Enable
    public const ulong OSFXSR = 1UL << 9;       // OS supports FXSAVE/FXRSTOR
    public const ulong OSXMMEXCPT = 1UL << 10;  // OS supports SSE exceptions (#XM)
    public const ulong UMIP = 1UL << 11;        // User-Mode Instruction Prevention
    public const ulong OSXSAVE = 1UL << 18;     // OS supports XSAVE
    public const ulong SMEP = 1UL << 20;        // Supervisor Mode Execution Prevention
    public const ulong SMAP = 1UL << 21;        // Supervisor Mode Access Prevention
}

/// <summary>
/// XCR0 (Extended Control Register 0) bit definitions
/// </summary>
public static class XCR0
{
    public const ulong X87 = 1UL << 0;      // x87 FPU state
    public const ulong SSE = 1UL << 1;      // SSE state (XMM registers)
    public const ulong AVX = 1UL << 2;      // AVX state (YMM registers)
    public const ulong BNDREG = 1UL << 3;   // MPX bound registers
    public const ulong BNDCSR = 1UL << 4;   // MPX bound CSR
    public const ulong OPMASK = 1UL << 5;   // AVX-512 opmask
    public const ulong ZMM_HI256 = 1UL << 6; // AVX-512 upper 256 bits of ZMM0-15
    public const ulong HI16_ZMM = 1UL << 7;  // AVX-512 ZMM16-31
}

/// <summary>
/// EFER MSR bit definitions
/// </summary>
public static class EFER
{
    public const uint MSR = 0xC0000080;     // EFER MSR number
    public const ulong SCE = 1UL << 0;      // System Call Extensions
    public const ulong LME = 1UL << 8;      // Long Mode Enable
    public const ulong LMA = 1UL << 10;     // Long Mode Active (read-only)
    public const ulong NXE = 1UL << 11;     // No-Execute Enable
}

/// <summary>
/// Detected CPU feature flags
/// </summary>
public enum CPUFeatureFlags : ulong
{
    None = 0,

    // Basic features (CPUID.01H:EDX)
    FPU = 1UL << 0,         // x87 FPU on chip
    SSE = 1UL << 1,         // SSE extensions
    SSE2 = 1UL << 2,        // SSE2 extensions
    FXSR = 1UL << 3,        // FXSAVE/FXRSTOR support

    // Extended features (CPUID.01H:ECX)
    SSE3 = 1UL << 4,        // SSE3 extensions
    SSSE3 = 1UL << 5,       // Supplemental SSE3
    SSE41 = 1UL << 6,       // SSE4.1 extensions
    SSE42 = 1UL << 7,       // SSE4.2 extensions
    POPCNT = 1UL << 8,      // POPCNT instruction
    XSAVE = 1UL << 9,       // XSAVE/XRSTOR support
    OSXSAVE = 1UL << 10,    // XSAVE enabled by OS
    AVX = 1UL << 11,        // AVX support

    // Extended features (CPUID.07H:EBX)
    AVX2 = 1UL << 12,       // AVX2 support
    BMI1 = 1UL << 13,       // BMI1 instructions
    BMI2 = 1UL << 14,       // BMI2 instructions

    // Extended features (CPUID.80000001H:EDX)
    NX = 1UL << 15,         // No-Execute bit support
    LM = 1UL << 16,         // Long Mode (64-bit)
}

/// <summary>
/// CPU feature detection and initialization
/// </summary>
public static class CPUFeatures
{
    private static CPUFeatureFlags _detectedFeatures;
    private static bool _initialized;
    private static bool _sseEnabled;
    private static bool _avxEnabled;

    // XSAVE area sizes (determined by CPUID)
    private static uint _xsaveAreaSize;      // Total XSAVE area size for enabled features
    private static uint _xsaveLegacySize;    // Legacy region size (512 bytes for FXSAVE format)
    private static bool _useXsave;           // True if XSAVE is available and enabled

    /// <summary>
    /// Detected CPU features
    /// </summary>
    public static CPUFeatureFlags DetectedFeatures => _detectedFeatures;

    /// <summary>
    /// Whether SSE is enabled
    /// </summary>
    public static bool SSEEnabled => _sseEnabled;

    /// <summary>
    /// Whether AVX is enabled
    /// </summary>
    public static bool AVXEnabled => _avxEnabled;

    /// <summary>
    /// Whether XSAVE is being used (vs FXSAVE)
    /// </summary>
    public static bool UseXsave => _useXsave;

    /// <summary>
    /// Size of the extended state save area in bytes.
    /// This is the XSAVE area size if XSAVE is enabled, otherwise 512 for FXSAVE.
    /// </summary>
    public static uint ExtendedStateSize => _xsaveAreaSize;

    /// <summary>
    /// FXSAVE area size (legacy SSE state) - always 512 bytes
    /// </summary>
    public const uint FxsaveAreaSize = 512;

    /// <summary>
    /// Check if a specific feature is available
    /// </summary>
    public static bool HasFeature(CPUFeatureFlags feature)
        => (_detectedFeatures & feature) == feature;

    /// <summary>
    /// Detect and initialize CPU features.
    /// Should be called early in boot process.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[CPUFeatures] Detecting CPU features...");

        // Detect features via CPUID
        DetectFeatures();

        // Report detected features
        ReportFeatures();

        // Enable SSE (required for JIT-generated code)
        EnableSSE();

        // Enable NX bit if available
        EnableNX();

        _initialized = true;
        DebugConsole.WriteLine("[CPUFeatures] Initialization complete");
    }

    private static void DetectFeatures()
    {
        _detectedFeatures = CPUFeatureFlags.None;

        // Get max standard CPUID leaf
        CPU.Cpuid(0, out uint maxLeaf, out _, out _, out _);

        if (maxLeaf >= 1)
        {
            // CPUID.01H - Basic feature flags
            CPU.Cpuid(1, out _, out _, out uint ecx, out uint edx);

            // EDX features
            if ((edx & (1 << 0)) != 0) _detectedFeatures |= CPUFeatureFlags.FPU;
            if ((edx & (1 << 24)) != 0) _detectedFeatures |= CPUFeatureFlags.FXSR;
            if ((edx & (1 << 25)) != 0) _detectedFeatures |= CPUFeatureFlags.SSE;
            if ((edx & (1 << 26)) != 0) _detectedFeatures |= CPUFeatureFlags.SSE2;

            // ECX features
            if ((ecx & (1 << 0)) != 0) _detectedFeatures |= CPUFeatureFlags.SSE3;
            if ((ecx & (1 << 9)) != 0) _detectedFeatures |= CPUFeatureFlags.SSSE3;
            if ((ecx & (1 << 19)) != 0) _detectedFeatures |= CPUFeatureFlags.SSE41;
            if ((ecx & (1 << 20)) != 0) _detectedFeatures |= CPUFeatureFlags.SSE42;
            if ((ecx & (1 << 23)) != 0) _detectedFeatures |= CPUFeatureFlags.POPCNT;
            if ((ecx & (1 << 26)) != 0) _detectedFeatures |= CPUFeatureFlags.XSAVE;
            if ((ecx & (1 << 27)) != 0) _detectedFeatures |= CPUFeatureFlags.OSXSAVE;
            if ((ecx & (1 << 28)) != 0) _detectedFeatures |= CPUFeatureFlags.AVX;
        }

        if (maxLeaf >= 7)
        {
            // CPUID.07H - Extended features
            CPU.Cpuid(7, 0, out _, out uint ebx, out _, out _);

            if ((ebx & (1 << 3)) != 0) _detectedFeatures |= CPUFeatureFlags.BMI1;
            if ((ebx & (1 << 5)) != 0) _detectedFeatures |= CPUFeatureFlags.AVX2;
            if ((ebx & (1 << 8)) != 0) _detectedFeatures |= CPUFeatureFlags.BMI2;
        }

        // Get max extended CPUID leaf
        CPU.Cpuid(0x80000000, out uint maxExtLeaf, out _, out _, out _);

        if (maxExtLeaf >= 0x80000001)
        {
            // CPUID.80000001H - Extended feature flags
            CPU.Cpuid(0x80000001, out _, out _, out _, out uint edx2);

            if ((edx2 & (1 << 20)) != 0) _detectedFeatures |= CPUFeatureFlags.NX;
            if ((edx2 & (1 << 29)) != 0) _detectedFeatures |= CPUFeatureFlags.LM;
        }
    }

    private static void ReportFeatures()
    {
        DebugConsole.Write("[CPUFeatures] Detected: ");

        if (HasFeature(CPUFeatureFlags.FPU)) DebugConsole.Write("FPU ");
        if (HasFeature(CPUFeatureFlags.SSE)) DebugConsole.Write("SSE ");
        if (HasFeature(CPUFeatureFlags.SSE2)) DebugConsole.Write("SSE2 ");
        if (HasFeature(CPUFeatureFlags.SSE3)) DebugConsole.Write("SSE3 ");
        if (HasFeature(CPUFeatureFlags.SSSE3)) DebugConsole.Write("SSSE3 ");
        if (HasFeature(CPUFeatureFlags.SSE41)) DebugConsole.Write("SSE4.1 ");
        if (HasFeature(CPUFeatureFlags.SSE42)) DebugConsole.Write("SSE4.2 ");
        if (HasFeature(CPUFeatureFlags.AVX)) DebugConsole.Write("AVX ");
        if (HasFeature(CPUFeatureFlags.AVX2)) DebugConsole.Write("AVX2 ");
        if (HasFeature(CPUFeatureFlags.XSAVE)) DebugConsole.Write("XSAVE ");
        if (HasFeature(CPUFeatureFlags.NX)) DebugConsole.Write("NX ");
        if (HasFeature(CPUFeatureFlags.POPCNT)) DebugConsole.Write("POPCNT ");

        DebugConsole.WriteLine();

        // Report current CR0/CR4 state
        ulong cr0 = CPU.ReadCr0();
        ulong cr4 = CPU.ReadCr4();

        DebugConsole.Write("[CPUFeatures] CR0=0x");
        DebugConsole.WriteHex(cr0);
        DebugConsole.Write(" CR4=0x");
        DebugConsole.WriteHex(cr4);
        DebugConsole.WriteLine();
    }

    private static void EnableSSE()
    {
        if (!HasFeature(CPUFeatureFlags.SSE) || !HasFeature(CPUFeatureFlags.FXSR))
        {
            DebugConsole.WriteLine("[CPUFeatures] WARNING: SSE/FXSR not supported!");
            return;
        }

        // Configure CR0 for FPU/SSE
        // Clear EM (bit 2) - must be 0 for SSE
        // Set MP (bit 1) - monitor coprocessor
        // Set NE (bit 5) - native FPU exceptions
        ulong cr0 = CPU.ReadCr0();
        cr0 &= ~CR0.EM;      // Clear emulation mode
        cr0 |= CR0.MP;       // Set monitor coprocessor
        cr0 |= CR0.NE;       // Set native exceptions
        CPU.WriteCr0(cr0);

        // Configure CR4 for SSE
        // Set OSFXSR (bit 9) - OS supports FXSAVE/FXRSTOR
        // Set OSXMMEXCPT (bit 10) - OS supports SSE exceptions
        ulong cr4 = CPU.ReadCr4();
        cr4 |= CR4.OSFXSR;       // Enable FXSAVE/FXRSTOR
        cr4 |= CR4.OSXMMEXCPT;   // Enable SSE exceptions
        CPU.WriteCr4(cr4);

        // Initialize FPU
        CPU.InitFpu();

        _sseEnabled = true;
        _useXsave = false;
        _xsaveAreaSize = FxsaveAreaSize;  // Default to FXSAVE size (512 bytes)
        _xsaveLegacySize = FxsaveAreaSize;

        DebugConsole.WriteLine("[CPUFeatures] SSE enabled (CR0.EM=0, CR4.OSFXSR=1, CR4.OSXMMEXCPT=1)");

        // Optionally enable AVX if supported
        if (HasFeature(CPUFeatureFlags.AVX) && HasFeature(CPUFeatureFlags.XSAVE))
        {
            EnableAVX();
        }
    }

    private static void EnableAVX()
    {
        // Enable XSAVE first
        ulong cr4 = CPU.ReadCr4();
        cr4 |= CR4.OSXSAVE;
        CPU.WriteCr4(cr4);

        // Now set XCR0 to enable AVX state
        // XCR0[0] = x87 state (always required)
        // XCR0[1] = SSE state
        // XCR0[2] = AVX state
        ulong xcr0 = XCR0.X87 | XCR0.SSE | XCR0.AVX;
        CPU.WriteXcr0(xcr0);

        _avxEnabled = true;
        _useXsave = true;

        // Query XSAVE area size using CPUID.0DH
        // Subleaf 0 with XCR0 bits returns the required XSAVE area size
        CPU.Cpuid(0x0D, 0, out uint eax, out uint ebx, out uint ecx, out _);

        // EBX = size required for all enabled features (in XCR0)
        // ECX = maximum size for all supported features
        // EAX = valid bits for XCR0 (lower 32 bits)
        _xsaveAreaSize = ebx;
        _xsaveLegacySize = FxsaveAreaSize;

        DebugConsole.Write("[CPUFeatures] AVX enabled (CR4.OSXSAVE=1, XCR0=0x7), XSAVE area size: ");
        DebugConsole.WriteDecimal(_xsaveAreaSize);
        DebugConsole.WriteLine(" bytes");
    }

    private static void EnableNX()
    {
        if (!HasFeature(CPUFeatureFlags.NX))
        {
            DebugConsole.WriteLine("[CPUFeatures] NX bit not supported");
            return;
        }

        // Enable NX bit in EFER MSR
        ulong efer = CPU.ReadMsr(EFER.MSR);
        if ((efer & EFER.NXE) == 0)
        {
            efer |= EFER.NXE;
            CPU.WriteMsr(EFER.MSR, efer);
            DebugConsole.WriteLine("[CPUFeatures] NX bit enabled (EFER.NXE=1)");
        }
        else
        {
            DebugConsole.WriteLine("[CPUFeatures] NX bit already enabled");
        }
    }

    /// <summary>
    /// Dump current CPU state for debugging
    /// </summary>
    public static void DumpState()
    {
        DebugConsole.WriteLine("[CPUFeatures] Current CPU State:");

        ulong cr0 = CPU.ReadCr0();
        DebugConsole.Write("  CR0: 0x");
        DebugConsole.WriteHex(cr0);
        DebugConsole.Write(" (PE=");
        DebugConsole.Write((cr0 & CR0.PE) != 0 ? "1" : "0");
        DebugConsole.Write(" MP=");
        DebugConsole.Write((cr0 & CR0.MP) != 0 ? "1" : "0");
        DebugConsole.Write(" EM=");
        DebugConsole.Write((cr0 & CR0.EM) != 0 ? "1" : "0");
        DebugConsole.Write(" TS=");
        DebugConsole.Write((cr0 & CR0.TS) != 0 ? "1" : "0");
        DebugConsole.Write(" NE=");
        DebugConsole.Write((cr0 & CR0.NE) != 0 ? "1" : "0");
        DebugConsole.Write(" PG=");
        DebugConsole.Write((cr0 & CR0.PG) != 0 ? "1" : "0");
        DebugConsole.WriteLine(")");

        ulong cr4 = CPU.ReadCr4();
        DebugConsole.Write("  CR4: 0x");
        DebugConsole.WriteHex(cr4);
        DebugConsole.Write(" (PAE=");
        DebugConsole.Write((cr4 & CR4.PAE) != 0 ? "1" : "0");
        DebugConsole.Write(" OSFXSR=");
        DebugConsole.Write((cr4 & CR4.OSFXSR) != 0 ? "1" : "0");
        DebugConsole.Write(" OSXMMEXCPT=");
        DebugConsole.Write((cr4 & CR4.OSXMMEXCPT) != 0 ? "1" : "0");
        DebugConsole.Write(" OSXSAVE=");
        DebugConsole.Write((cr4 & CR4.OSXSAVE) != 0 ? "1" : "0");
        DebugConsole.WriteLine(")");

        ulong efer = CPU.ReadMsr(EFER.MSR);
        DebugConsole.Write("  EFER: 0x");
        DebugConsole.WriteHex(efer);
        DebugConsole.Write(" (LME=");
        DebugConsole.Write((efer & EFER.LME) != 0 ? "1" : "0");
        DebugConsole.Write(" LMA=");
        DebugConsole.Write((efer & EFER.LMA) != 0 ? "1" : "0");
        DebugConsole.Write(" NXE=");
        DebugConsole.Write((efer & EFER.NXE) != 0 ? "1" : "0");
        DebugConsole.WriteLine(")");

        if (_avxEnabled)
        {
            ulong xcr0 = CPU.ReadXcr0();
            DebugConsole.Write("  XCR0: 0x");
            DebugConsole.WriteHex(xcr0);
            DebugConsole.WriteLine();
        }
    }
}
