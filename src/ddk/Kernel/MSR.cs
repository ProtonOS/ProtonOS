// ProtonOS DDK - MSR Kernel Wrappers
// DllImport wrappers for x86 Model Specific Register operations.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// DDK wrappers for x86 MSR (Model Specific Register) operations.
/// Only available on x86/x64 architecture.
/// </summary>
public static class MSR
{
    [DllImport("*", EntryPoint = "Kernel_ReadMSR")]
    public static extern ulong Read(uint msr);

    [DllImport("*", EntryPoint = "Kernel_WriteMSR")]
    public static extern void Write(uint msr, ulong value);

    // Common MSR indices
    public const uint IA32_APIC_BASE = 0x1B;
    public const uint IA32_FEATURE_CONTROL = 0x3A;
    public const uint IA32_TSC = 0x10;
    public const uint IA32_MTRRCAP = 0xFE;
    public const uint IA32_SYSENTER_CS = 0x174;
    public const uint IA32_SYSENTER_ESP = 0x175;
    public const uint IA32_SYSENTER_EIP = 0x176;
    public const uint IA32_PAT = 0x277;
    public const uint IA32_EFER = 0xC0000080;
    public const uint IA32_STAR = 0xC0000081;
    public const uint IA32_LSTAR = 0xC0000082;
    public const uint IA32_CSTAR = 0xC0000083;
    public const uint IA32_FMASK = 0xC0000084;
    public const uint IA32_FS_BASE = 0xC0000100;
    public const uint IA32_GS_BASE = 0xC0000101;
    public const uint IA32_KERNEL_GS_BASE = 0xC0000102;
    public const uint IA32_TSC_AUX = 0xC0000103;

    // EFER bits
    public const ulong EFER_SCE = 1UL << 0;   // System Call Enable
    public const ulong EFER_LME = 1UL << 8;   // Long Mode Enable
    public const ulong EFER_LMA = 1UL << 10;  // Long Mode Active
    public const ulong EFER_NXE = 1UL << 11;  // No-Execute Enable
}
