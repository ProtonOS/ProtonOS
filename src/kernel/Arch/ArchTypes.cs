// ProtonOS Architecture Abstraction - Compile-Time Dispatch
// Global using aliases for architecture selection.
// The correct architecture types are selected at compile time via preprocessor defines.

// Note: These global usings provide convenient aliases so that kernel code can write:
//   CurrentCpu.DisableInterrupts();
//   CurrentEmitter.EmitPrologue(ref code, 16);
// Instead of:
//   #if ARCH_X64
//   ProtonOS.X64.CPU.DisableInterrupts();
//   #endif

#if ARCH_X64

global using CurrentArch = ProtonOS.X64.Arch;
global using CurrentCpu = ProtonOS.X64.CPU;
global using CurrentVMem = ProtonOS.X64.VirtualMemory;
// Note: X64Emitter will implement ICodeEmitter<X64Emitter>
// global using CurrentEmitter = ProtonOS.X64.X64Emitter;  // Uncomment when X64Emitter implements interface

#elif ARCH_ARM64

// Future: ARM64 implementation
// global using CurrentArch = ProtonOS.Arm64.Arch;
// global using CurrentCpu = ProtonOS.Arm64.Cpu;
// global using CurrentVMem = ProtonOS.Arm64.VirtualMemory;
// global using CurrentEmitter = ProtonOS.Arm64.Arm64Emitter;

#else

// Default: Assume x64 if no architecture specified
// This allows existing code to continue working without -d ARCH_X64

#endif

namespace ProtonOS.Arch;

/// <summary>
/// Architecture detection utilities.
/// Provides compile-time constants for architecture selection.
/// </summary>
public static class ArchInfo
{
#if ARCH_X64
    public const string Name = "x64";
    public const bool IsX64 = true;
    public const bool IsArm64 = false;
    public const int PointerSize = 8;
    public const int RegisterCount = 16;
    public const int FloatRegisterCount = 16;
#elif ARCH_ARM64
    public const string Name = "arm64";
    public const bool IsX64 = false;
    public const bool IsArm64 = true;
    public const int PointerSize = 8;
    public const int RegisterCount = 31;
    public const int FloatRegisterCount = 32;
#else
    // Default to x64 for backwards compatibility
    public const string Name = "x64";
    public const bool IsX64 = true;
    public const bool IsArm64 = false;
    public const int PointerSize = 8;
    public const int RegisterCount = 16;
    public const int FloatRegisterCount = 16;
#endif
}
