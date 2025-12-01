// ProtonOS kernel - ReadyToRun Header Access
// Provides access to NativeAOT runtime metadata including GC info, static roots, and type information.
//
// The ReadyToRun (RTR) header is the central registry for all NativeAOT runtime metadata.
// It's located via the .modules PE section which contains a pointer to the header.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Runtime;

/// <summary>
/// ReadyToRun section types used by NativeAOT.
/// Values 200+ are NativeAOT-specific extensions to the ReadyToRun format.
/// </summary>
public enum ReadyToRunSectionType : uint
{
    // Standard ReadyToRun sections (100-199)
    CompilerIdentifier = 100,
    ImportSections = 101,
    RuntimeFunctions = 102,
    MethodDefEntryPoints = 103,
    ExceptionInfo = 104,
    DebugInfo = 105,
    DelayLoadMethodCallThunks = 106,
    // ... more standard sections ...

    // NativeAOT-specific sections (200+)
    StringTable = 200,
    GCStaticRegion = 201,
    ThreadStaticRegion = 202,
    InterfaceDispatchTable = 203,
    TypeManagerIndirection = 204,
    EagerCctor = 205,
    FrozenObjectRegion = 206,
    DehydratedData = 207,
    ThreadStaticOffsetRegion = 208,
    ThreadStaticGCDescRegion = 209,
    ThreadStaticIndex = 210,
    LoopHijackFlag = 211,
    ImportAddressTables = 212,
    ModuleInitializerList = 213,

    // Readonly blob sections (300+)
    ReadonlyBlobRegionStart = 300,
    ReadonlyBlobRegionEnd = 399,
}

/// <summary>
/// Flags for ModuleInfoRow entries.
/// </summary>
public enum ModuleInfoFlags : uint
{
    None = 0,
    HasEndPointer = 1,  // Entry has both start and end pointers (vs just start)
}

/// <summary>
/// ReadyToRun header structure.
/// Located via .modules section pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ReadyToRunHeader
{
    public uint Signature;          // 'RTR\0' = 0x00525452
    public ushort MajorVersion;     // Currently 16
    public ushort MinorVersion;     // Currently 0
    public uint Flags;
    public ushort NumberOfSections;
    public byte EntrySize;          // Size of each section entry (24 bytes for pointer-based)
    public byte EntryType;          // 1 = pointer-based entries

    public const uint ExpectedSignature = 0x00525452; // 'RTR\0'
    public const ushort CurrentMajorVersion = 16;

    public bool IsValid => Signature == ExpectedSignature;
}

/// <summary>
/// Section entry in the RTR header (pointer-based format, EntryType=1).
/// Each entry is 24 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ModuleInfoRow
{
    public uint SectionId;          // ReadyToRunSectionType
    public uint Flags;              // ModuleInfoFlags
    public void* Start;             // Absolute VA of section start
    public void* End;               // Absolute VA of section end (if HasEndPointer)

    public ReadyToRunSectionType Type => (ReadyToRunSectionType)SectionId;
    public bool HasEndPointer => (Flags & (uint)ModuleInfoFlags.HasEndPointer) != 0;
    public nuint Size => HasEndPointer ? (nuint)((byte*)End - (byte*)Start) : 0;
}

/// <summary>
/// Provides access to the kernel's ReadyToRun header and its sections.
/// This is the primary entry point for runtime metadata access.
/// </summary>
public static unsafe class ReadyToRunInfo
{
    private static bool _initialized;
    private static ReadyToRunHeader* _header;
    private static ModuleInfoRow* _sections;
    private static int _sectionCount;
    private static ulong _imageBase;

    // Cached section pointers for frequently accessed sections
    private static void* _gcStaticRegionStart;
    private static void* _gcStaticRegionEnd;
    private static void* _frozenObjectStart;
    private static void* _frozenObjectEnd;
    private static void** _typeManagerIndirection;

    /// <summary>
    /// Initialize RTR info from the kernel's PE image.
    /// Must be called after UEFIBoot.Init() sets the image base.
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        _imageBase = UEFIBoot.ImageBase;
        if (_imageBase == 0)
        {
            DebugConsole.WriteLine("[RTR] Error: Image base not set");
            return;
        }

        // Find .modules section via PE header
        if (!FindModulesSection(out var modulesPtr))
        {
            DebugConsole.WriteLine("[RTR] Error: .modules section not found");
            return;
        }

        // .modules contains a pointer to the RTR header
        _header = *(ReadyToRunHeader**)modulesPtr;

        if (_header == null)
        {
            DebugConsole.WriteLine("[RTR] Error: RTR header pointer is null");
            return;
        }

        // Validate header
        if (!_header->IsValid)
        {
            DebugConsole.Write("[RTR] Error: Invalid RTR signature: 0x");
            DebugConsole.WriteHex(_header->Signature);
            DebugConsole.WriteLine();
            return;
        }

        if (_header->MajorVersion != ReadyToRunHeader.CurrentMajorVersion)
        {
            DebugConsole.Write("[RTR] Warning: RTR version ");
            DebugConsole.WriteDecimal(_header->MajorVersion);
            DebugConsole.Write(".");
            DebugConsole.WriteDecimal(_header->MinorVersion);
            DebugConsole.Write(" (expected ");
            DebugConsole.WriteDecimal(ReadyToRunHeader.CurrentMajorVersion);
            DebugConsole.WriteLine(".x)");
        }

        _sectionCount = _header->NumberOfSections;
        _sections = (ModuleInfoRow*)((byte*)_header + sizeof(ReadyToRunHeader));

        // Cache frequently-used sections
        CacheCommonSections();

        _initialized = true;

        DebugConsole.Write("[RTR] Initialized: ");
        DebugConsole.WriteDecimal(_sectionCount);
        DebugConsole.Write(" sections, header at 0x");
        DebugConsole.WriteHex((ulong)_header);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Find the .modules PE section.
    /// </summary>
    private static bool FindModulesSection(out void* sectionData)
    {
        sectionData = null;

        var dosHeader = (ImageDosHeader*)_imageBase;
        if (dosHeader->e_magic != 0x5A4D) // "MZ"
            return false;

        var ntHeaders = (ImageNtHeaders64*)(_imageBase + (uint)dosHeader->e_lfanew);
        if (ntHeaders->Signature != 0x00004550) // "PE\0\0"
            return false;

        // Get section headers (immediately after optional header)
        int sectionCount = ntHeaders->FileHeader.NumberOfSections;
        var sectionHeaders = (ImageSectionHeader*)((byte*)&ntHeaders->OptionalHeader +
            ntHeaders->FileHeader.SizeOfOptionalHeader);

        // Look for .modules section
        for (int i = 0; i < sectionCount; i++)
        {
            // Compare section name (8 bytes, null-padded)
            if (sectionHeaders[i].Name0 == 0x646F6D2E && // ".mod"
                sectionHeaders[i].Name1 == 0x73656C75)   // "ules"
            {
                sectionData = (void*)(_imageBase + sectionHeaders[i].VirtualAddress);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cache pointers to commonly-accessed sections.
    /// </summary>
    private static void CacheCommonSections()
    {
        for (int i = 0; i < _sectionCount; i++)
        {
            switch (_sections[i].Type)
            {
                case ReadyToRunSectionType.GCStaticRegion:
                    _gcStaticRegionStart = _sections[i].Start;
                    _gcStaticRegionEnd = _sections[i].End;
                    break;

                case ReadyToRunSectionType.FrozenObjectRegion:
                    _frozenObjectStart = _sections[i].Start;
                    _frozenObjectEnd = _sections[i].End;
                    break;

                case ReadyToRunSectionType.TypeManagerIndirection:
                    _typeManagerIndirection = (void**)_sections[i].Start;
                    break;
            }
        }
    }

    /// <summary>
    /// Get a section by type.
    /// </summary>
    /// <param name="type">Section type to find</param>
    /// <param name="start">Receives section start pointer</param>
    /// <param name="end">Receives section end pointer (may be null if no end pointer)</param>
    /// <returns>True if section found</returns>
    public static bool TryGetSection(ReadyToRunSectionType type, out void* start, out void* end)
    {
        start = null;
        end = null;

        if (!_initialized) return false;

        for (int i = 0; i < _sectionCount; i++)
        {
            if (_sections[i].Type == type)
            {
                start = _sections[i].Start;
                end = _sections[i].HasEndPointer ? _sections[i].End : null;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Get the GC static region bounds.
    /// The GC static region contains pointers to static fields that hold object references.
    /// </summary>
    public static bool GetGCStaticRegion(out void* start, out void* end)
    {
        start = _gcStaticRegionStart;
        end = _gcStaticRegionEnd;
        return start != null;
    }

    /// <summary>
    /// Get the frozen object region bounds.
    /// Frozen objects are pre-allocated at compile time (e.g., string literals).
    /// </summary>
    public static bool GetFrozenObjectRegion(out void* start, out void* end)
    {
        start = _frozenObjectStart;
        end = _frozenObjectEnd;
        return start != null;
    }

    /// <summary>
    /// Get the type manager indirection pointer.
    /// </summary>
    public static void** GetTypeManagerIndirection()
    {
        return _typeManagerIndirection;
    }

    /// <summary>
    /// Dump all sections for debugging.
    /// </summary>
    public static void DumpSections()
    {
        if (!_initialized)
        {
            DebugConsole.WriteLine("[RTR] Not initialized");
            return;
        }

        DebugConsole.WriteLine("[RTR] Section dump:");
        for (int i = 0; i < _sectionCount; i++)
        {
            DebugConsole.Write("  [");
            DebugConsole.WriteDecimal(i);
            DebugConsole.Write("] Type=");
            DebugConsole.WriteDecimal(_sections[i].SectionId);
            DebugConsole.Write(" Start=0x");
            DebugConsole.WriteHex((ulong)_sections[i].Start);

            if (_sections[i].HasEndPointer)
            {
                DebugConsole.Write(" End=0x");
                DebugConsole.WriteHex((ulong)_sections[i].End);
                DebugConsole.Write(" Size=");
                DebugConsole.WriteDecimal((uint)_sections[i].Size);
            }

            // Print known section names
            switch (_sections[i].Type)
            {
                case ReadyToRunSectionType.GCStaticRegion:
                    DebugConsole.Write(" (GCStaticRegion)");
                    break;
                case ReadyToRunSectionType.FrozenObjectRegion:
                    DebugConsole.Write(" (FrozenObjects)");
                    break;
                case ReadyToRunSectionType.TypeManagerIndirection:
                    DebugConsole.Write(" (TypeManager)");
                    break;
                case ReadyToRunSectionType.EagerCctor:
                    DebugConsole.Write(" (EagerCctor)");
                    break;
                case ReadyToRunSectionType.ModuleInitializerList:
                    DebugConsole.Write(" (ModuleInit)");
                    break;
                case ReadyToRunSectionType.ThreadStaticRegion:
                    DebugConsole.Write(" (ThreadStatic)");
                    break;
                default:
                    if (_sections[i].SectionId >= 300 && _sections[i].SectionId < 400)
                        DebugConsole.Write(" (ReadonlyBlob)");
                    break;
            }

            DebugConsole.WriteLine();
        }

        // Also dump GCInfo samples
        GCInfoHelper.DumpSamples(_imageBase);

        // Run comprehensive validation
        GCInfoHelper.ValidateComprehensive(_imageBase);
    }

    /// <summary>
    /// Whether RTR info has been successfully initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Get the image base address.
    /// </summary>
    public static ulong ImageBase => _imageBase;

    /// <summary>
    /// Get the RTR header pointer.
    /// </summary>
    public static ReadyToRunHeader* Header => _header;
}
