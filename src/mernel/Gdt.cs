// netos mernel - Global Descriptor Table
// In 64-bit long mode, segmentation is mostly disabled but GDT is still required
// for privilege levels, TSS, and the syscall/sysret instructions.

using System.Runtime.InteropServices;

namespace Mernel;

/// <summary>
/// GDT segment selectors (byte offsets into GDT)
/// Layout optimized for syscall/sysret instructions.
/// </summary>
public static class GdtSelectors
{
    public const ushort Null = 0x00;
    public const ushort KernelCode = 0x08;
    public const ushort KernelData = 0x10;
    public const ushort UserData = 0x18;   // Before UserCode for sysret
    public const ushort UserCode = 0x20;
    public const ushort Tss = 0x28;        // 16 bytes (2 GDT slots)
}

/// <summary>
/// 8-byte GDT entry structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GdtEntry
{
    public ushort LimitLow;      // Limit bits 0-15
    public ushort BaseLow;       // Base bits 0-15
    public byte BaseMid;         // Base bits 16-23
    public byte Access;          // Access byte
    public byte FlagsAndLimitHi; // Flags (4 bits) + Limit bits 16-19
    public byte BaseHigh;        // Base bits 24-31

    /// <summary>
    /// Create a null descriptor
    /// </summary>
    public static GdtEntry Null() => default;

    /// <summary>
    /// Create a 64-bit kernel code segment descriptor (Ring 0)
    /// </summary>
    public static GdtEntry KernelCode64()
    {
        return new GdtEntry
        {
            LimitLow = 0,
            BaseLow = 0,
            BaseMid = 0,
            // Access: Present=1, DPL=0, S=1, Type=Code(Execute/Read)
            // 1 00 1 1010 = 0x9A
            Access = 0x9A,
            // Flags: G=0, L=1 (64-bit), D=0, AVL=0 + Limit high=0
            // 0010 0000 = 0x20
            FlagsAndLimitHi = 0x20,
            BaseHigh = 0,
        };
    }

    /// <summary>
    /// Create a 64-bit kernel data segment descriptor (Ring 0)
    /// </summary>
    public static GdtEntry KernelData64()
    {
        return new GdtEntry
        {
            LimitLow = 0,
            BaseLow = 0,
            BaseMid = 0,
            // Access: Present=1, DPL=0, S=1, Type=Data(Read/Write)
            // 1 00 1 0010 = 0x92
            Access = 0x92,
            // Flags: G=0, L=0, D=0, AVL=0 + Limit high=0
            FlagsAndLimitHi = 0x00,
            BaseHigh = 0,
        };
    }

    /// <summary>
    /// Create a 64-bit user code segment descriptor (Ring 3)
    /// </summary>
    public static GdtEntry UserCode64()
    {
        return new GdtEntry
        {
            LimitLow = 0,
            BaseLow = 0,
            BaseMid = 0,
            // Access: Present=1, DPL=3, S=1, Type=Code(Execute/Read)
            // 1 11 1 1010 = 0xFA
            Access = 0xFA,
            // Flags: G=0, L=1 (64-bit), D=0, AVL=0 + Limit high=0
            FlagsAndLimitHi = 0x20,
            BaseHigh = 0,
        };
    }

    /// <summary>
    /// Create a 64-bit user data segment descriptor (Ring 3)
    /// </summary>
    public static GdtEntry UserData64()
    {
        return new GdtEntry
        {
            LimitLow = 0,
            BaseLow = 0,
            BaseMid = 0,
            // Access: Present=1, DPL=3, S=1, Type=Data(Read/Write)
            // 1 11 1 0010 = 0xF2
            Access = 0xF2,
            // Flags: G=0, L=0, D=0, AVL=0 + Limit high=0
            FlagsAndLimitHi = 0x00,
            BaseHigh = 0,
        };
    }
}

/// <summary>
/// 16-byte TSS descriptor for 64-bit mode (occupies 2 GDT slots)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TssDescriptor
{
    public ushort LimitLow;      // Limit bits 0-15
    public ushort BaseLow;       // Base bits 0-15
    public byte BaseMid;         // Base bits 16-23
    public byte Access;          // Access byte
    public byte FlagsAndLimitHi; // Flags (4 bits) + Limit bits 16-19
    public byte BaseHigh;        // Base bits 24-31
    public uint BaseUpper;       // Base bits 32-63
    public uint Reserved;        // Must be zero

    /// <summary>
    /// Create a TSS descriptor pointing to a TSS structure
    /// </summary>
    public static TssDescriptor Create(ulong baseAddress, ushort limit)
    {
        return new TssDescriptor
        {
            LimitLow = limit,
            BaseLow = (ushort)(baseAddress & 0xFFFF),
            BaseMid = (byte)((baseAddress >> 16) & 0xFF),
            // Access: Present=1, DPL=0, Type=Available 64-bit TSS
            // 1 00 0 1001 = 0x89
            Access = 0x89,
            // Flags: G=0, AVL=0 + Limit high bits
            FlagsAndLimitHi = 0x00,
            BaseHigh = (byte)((baseAddress >> 24) & 0xFF),
            BaseUpper = (uint)((baseAddress >> 32) & 0xFFFFFFFF),
            Reserved = 0,
        };
    }
}

/// <summary>
/// 64-bit Task State Segment
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Tss64
{
    public uint Reserved0;
    public ulong Rsp0;           // Stack pointer for Ring 0
    public ulong Rsp1;           // Stack pointer for Ring 1
    public ulong Rsp2;           // Stack pointer for Ring 2
    public ulong Reserved1;
    public ulong Ist1;           // Interrupt Stack Table 1
    public ulong Ist2;
    public ulong Ist3;
    public ulong Ist4;
    public ulong Ist5;
    public ulong Ist6;
    public ulong Ist7;
    public ulong Reserved2;
    public ushort Reserved3;
    public ushort IoMapBase;     // Offset to I/O permission bitmap
}

/// <summary>
/// GDT pointer structure for lgdt instruction
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GdtPointer
{
    public ushort Limit;  // Size of GDT - 1
    public ulong Base;    // Linear address of GDT
}

/// <summary>
/// Global Descriptor Table management
/// </summary>
public static unsafe class Gdt
{
    // GDT layout: null + kernel code + kernel data + user data + user code + TSS (2 slots)
    // Total: 7 GdtEntry slots (TSS takes 2)
    private const int GdtEntryCount = 7;

    // Static storage for GDT and TSS (must not move in memory)
    private static GdtEntry* _gdt;
    private static Tss64* _tss;
    private static GdtPointer _gdtPointer;

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void lgdt(void* gdtPtr);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void reload_segments(ushort codeSelector, ushort dataSelector);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ltr(ushort selector);

    /// <summary>
    /// Initialize and load our GDT
    /// </summary>
    public static void Init()
    {
        // Allocate GDT entries
        _gdt = (GdtEntry*)NativeMemory.Alloc((nuint)(GdtEntryCount * sizeof(GdtEntry)));

        // Allocate TSS
        _tss = (Tss64*)NativeMemory.Alloc((nuint)sizeof(Tss64));

        // Clear TSS
        for (int i = 0; i < sizeof(Tss64); i++)
            ((byte*)_tss)[i] = 0;

        // Set up GDT entries
        _gdt[0] = GdtEntry.Null();                              // 0x00: Null
        _gdt[1] = GdtEntry.KernelCode64();                      // 0x08: Kernel Code
        _gdt[2] = GdtEntry.KernelData64();                      // 0x10: Kernel Data
        _gdt[3] = GdtEntry.UserData64();                        // 0x18: User Data
        _gdt[4] = GdtEntry.UserCode64();                        // 0x20: User Code

        // TSS descriptor (16 bytes = 2 GDT slots)
        var tssDesc = TssDescriptor.Create((ulong)_tss, (ushort)(sizeof(Tss64) - 1));
        *(TssDescriptor*)&_gdt[5] = tssDesc;                    // 0x28: TSS

        // Set up GDT pointer
        _gdtPointer.Limit = (ushort)(GdtEntryCount * sizeof(GdtEntry) - 1);
        _gdtPointer.Base = (ulong)_gdt;

        // Load the GDT
        fixed (GdtPointer* ptr = &_gdtPointer)
        {
            lgdt(ptr);
        }

        // Reload segment registers with our selectors
        reload_segments(GdtSelectors.KernelCode, GdtSelectors.KernelData);

        // Load the TSS
        ltr(GdtSelectors.Tss);

        DebugConsole.Write("[GDT] Loaded at 0x");
        DebugConsole.WriteHex((ulong)_gdt);
        DebugConsole.Write(", TSS at 0x");
        DebugConsole.WriteHex((ulong)_tss);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Set the kernel stack pointer (RSP0) used when transitioning to Ring 0
    /// </summary>
    public static void SetKernelStack(ulong rsp0)
    {
        _tss->Rsp0 = rsp0;
    }

    /// <summary>
    /// Set an Interrupt Stack Table entry
    /// </summary>
    public static void SetIst(int index, ulong stackPointer)
    {
        if (index < 1 || index > 7)
            return;

        ulong* ist = &_tss->Ist1;
        ist[index - 1] = stackPointer;
    }
}
