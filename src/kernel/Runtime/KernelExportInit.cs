// ProtonOS kernel - Kernel Export Initialization
// Registers all kernel exports for PInvoke resolution.

using System;
using ProtonOS.Exports.DDK;

namespace ProtonOS.Runtime;

using InterlockedExports = ProtonOS.Exports.DDK.InterlockedExports;

/// <summary>
/// Initializes kernel exports at startup.
/// </summary>
public static unsafe class KernelExportInit
{
    /// <summary>
    /// Initialize and register all kernel exports.
    /// </summary>
    public static void Initialize()
    {
        KernelExportRegistry.Initialize();

        // Register Port I/O exports
        RegisterPortIOExports();

        // Register Memory exports
        RegisterMemoryExports();

        // Register Debug exports
        RegisterDebugExports();

        // Register PCI exports
        RegisterPCIExports();

        // Register Interlocked exports
        RegisterInterlockedExports();

        // Register Thread exports
        RegisterThreadExports();

        // Register Assembly Loader exports
        RegisterAssemblyLoaderExports();

        KernelExportRegistry.DebugPrint();
    }

    private static void RegisterPortIOExports()
    {
        byte* n = stackalloc byte[32];

        // Kernel_InByte
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x49; n[8]=0x6E; n[9]=0x42; n[10]=0x79; n[11]=0x74; n[12]=0x65; n[13]=0; // InByte
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, byte>)&PortIOExports.InByte);

        // Kernel_OutByte
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x4F; n[8]=0x75; n[9]=0x74; n[10]=0x42; n[11]=0x79; n[12]=0x74; n[13]=0x65; n[14]=0; // OutByte
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, byte, void>)&PortIOExports.OutByte);

        // Kernel_InWord
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x49; n[8]=0x6E; n[9]=0x57; n[10]=0x6F; n[11]=0x72; n[12]=0x64; n[13]=0; // InWord
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, ushort>)&PortIOExports.InWord);

        // Kernel_OutWord
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x4F; n[8]=0x75; n[9]=0x74; n[10]=0x57; n[11]=0x6F; n[12]=0x72; n[13]=0x64; n[14]=0; // OutWord
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, ushort, void>)&PortIOExports.OutWord);

        // Kernel_InDword
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x49; n[8]=0x6E; n[9]=0x44; n[10]=0x77; n[11]=0x6F; n[12]=0x72; n[13]=0x64; n[14]=0; // InDword
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, uint>)&PortIOExports.InDword);

        // Kernel_OutDword
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x4F; n[8]=0x75; n[9]=0x74; n[10]=0x44; n[11]=0x77; n[12]=0x6F; n[13]=0x72; n[14]=0x64; n[15]=0; // OutDword
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, uint, void>)&PortIOExports.OutDword);
    }

    private static void RegisterMemoryExports()
    {
        byte* n = stackalloc byte[32];

        // Kernel_AllocatePage
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x41; n[8]=0x6C; n[9]=0x6C; n[10]=0x6F; n[11]=0x63; n[12]=0x61; n[13]=0x74; n[14]=0x65; // Allocate
        n[15]=0x50; n[16]=0x61; n[17]=0x67; n[18]=0x65; n[19]=0; // Page
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong>)&MemoryExports.AllocatePage);

        // Kernel_AllocatePages
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x41; n[8]=0x6C; n[9]=0x6C; n[10]=0x6F; n[11]=0x63; n[12]=0x61; n[13]=0x74; n[14]=0x65;
        n[15]=0x50; n[16]=0x61; n[17]=0x67; n[18]=0x65; n[19]=0x73; n[20]=0; // Pages
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, ulong>)&MemoryExports.AllocatePages);

        // Kernel_FreePage
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x46; n[8]=0x72; n[9]=0x65; n[10]=0x65; n[11]=0x50; n[12]=0x61; n[13]=0x67; n[14]=0x65; n[15]=0; // FreePage
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, void>)&MemoryExports.FreePage);

        // Kernel_FreePages
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x46; n[8]=0x72; n[9]=0x65; n[10]=0x65; n[11]=0x50; n[12]=0x61; n[13]=0x67; n[14]=0x65; n[15]=0x73; n[16]=0; // FreePages
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, ulong, void>)&MemoryExports.FreePages);

        // Kernel_PhysToVirt
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x68; n[9]=0x79; n[10]=0x73; n[11]=0x54; n[12]=0x6F; n[13]=0x56; n[14]=0x69; n[15]=0x72; n[16]=0x74; n[17]=0; // PhysToVirt
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, ulong>)&MemoryExports.PhysToVirt);

        // Kernel_VirtToPhys
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x56; n[8]=0x69; n[9]=0x72; n[10]=0x74; n[11]=0x54; n[12]=0x6F; n[13]=0x50; n[14]=0x68; n[15]=0x79; n[16]=0x73; n[17]=0; // VirtToPhys
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, ulong>)&MemoryExports.VirtToPhys);

        // Kernel_MapMMIO
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x4D; n[8]=0x61; n[9]=0x70; n[10]=0x4D; n[11]=0x4D; n[12]=0x49; n[13]=0x4F; n[14]=0; // MapMMIO
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, ulong, ulong>)&MemoryExports.MapMMIO);

        // Kernel_UnmapMMIO
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x55; n[8]=0x6E; n[9]=0x6D; n[10]=0x61; n[11]=0x70; n[12]=0x4D; n[13]=0x4D; n[14]=0x49; n[15]=0x4F; n[16]=0; // UnmapMMIO
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, ulong, void>)&MemoryExports.UnmapMMIO);
    }

    private static void RegisterDebugExports()
    {
        byte* n = stackalloc byte[32];

        // Kernel_DebugWrite
        // "Kernel_DebugWrite" = 4B 65 72 6E 65 6C 5F 44 65 62 75 67 57 72 69 74 65
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67; // Debug
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65; n[17]=0; // Write
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<char*, int, void>)&DebugExports.DebugWrite);

        // Kernel_DebugWriteLine
        // "Kernel_DebugWriteLine" = 4B 65 72 6E 65 6C 5F 44 65 62 75 67 57 72 69 74 65 4C 69 6E 65
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67; // Debug
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65; // Write
        n[17]=0x4C; n[18]=0x69; n[19]=0x6E; n[20]=0x65; n[21]=0; // Line
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<char*, int, void>)&DebugExports.DebugWriteLine);

        // Kernel_DebugWriteHex64
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67; // Debug
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65; // Write
        n[17]=0x48; n[18]=0x65; n[19]=0x78; n[20]=0x36; n[21]=0x34; n[22]=0; // Hex64
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, void>)&DebugExports.DebugWriteHex64);

        // Kernel_DebugWriteHex32
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67;
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65;
        n[17]=0x48; n[18]=0x65; n[19]=0x78; n[20]=0x33; n[21]=0x32; n[22]=0; // Hex32
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, void>)&DebugExports.DebugWriteHex32);

        // Kernel_DebugWriteHex16
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67;
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65;
        n[17]=0x48; n[18]=0x65; n[19]=0x78; n[20]=0x31; n[21]=0x36; n[22]=0; // Hex16
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ushort, void>)&DebugExports.DebugWriteHex16);

        // Kernel_DebugWriteHex8
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67;
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65;
        n[17]=0x48; n[18]=0x65; n[19]=0x78; n[20]=0x38; n[21]=0; // Hex8
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, void>)&DebugExports.DebugWriteHex8);

        // Kernel_DebugWriteDecimal (signed int32)
        // "Kernel_DebugWriteDecimal" = 4B 65 72 6E 65 6C 5F 44 65 62 75 67 57 72 69 74 65 44 65 63 69 6D 61 6C
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67; // Debug
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65; // Write
        n[17]=0x44; n[18]=0x65; n[19]=0x63; n[20]=0x69; n[21]=0x6D; n[22]=0x61; n[23]=0x6C; n[24]=0; // Decimal
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int, void>)&DebugExports.DebugWriteDecimal);

        // Kernel_DebugWriteDecimalU (unsigned int32)
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67;
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65;
        n[17]=0x44; n[18]=0x65; n[19]=0x63; n[20]=0x69; n[21]=0x6D; n[22]=0x61; n[23]=0x6C;
        n[24]=0x55; n[25]=0; // DecimalU
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, void>)&DebugExports.DebugWriteDecimalU);

        // Kernel_DebugWriteDecimal64 (unsigned int64)
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x44; n[8]=0x65; n[9]=0x62; n[10]=0x75; n[11]=0x67;
        n[12]=0x57; n[13]=0x72; n[14]=0x69; n[15]=0x74; n[16]=0x65;
        n[17]=0x44; n[18]=0x65; n[19]=0x63; n[20]=0x69; n[21]=0x6D; n[22]=0x61; n[23]=0x6C;
        n[24]=0x36; n[25]=0x34; n[26]=0; // Decimal64
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ulong, void>)&DebugExports.DebugWriteDecimal64);
    }

    private static void RegisterPCIExports()
    {
        byte* n = stackalloc byte[32];

        // Kernel_PciReadConfig32
        // "Kernel_PciReadConfig32" = 4B 65 72 6E 65 6C 5F 50 63 69 52 65 61 64 43 6F 6E 66 69 67 33 32
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x52; n[11]=0x65; n[12]=0x61; n[13]=0x64; // PciRead
        n[14]=0x43; n[15]=0x6F; n[16]=0x6E; n[17]=0x66; n[18]=0x69; n[19]=0x67; // Config
        n[20]=0x33; n[21]=0x32; n[22]=0; // 32
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, byte, uint>)&PCIExports.ReadConfig32);

        // Kernel_PciReadConfig16
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x52; n[11]=0x65; n[12]=0x61; n[13]=0x64;
        n[14]=0x43; n[15]=0x6F; n[16]=0x6E; n[17]=0x66; n[18]=0x69; n[19]=0x67;
        n[20]=0x31; n[21]=0x36; n[22]=0; // 16
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, byte, ushort>)&PCIExports.ReadConfig16);

        // Kernel_PciReadConfig8
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x52; n[11]=0x65; n[12]=0x61; n[13]=0x64;
        n[14]=0x43; n[15]=0x6F; n[16]=0x6E; n[17]=0x66; n[18]=0x69; n[19]=0x67;
        n[20]=0x38; n[21]=0; // 8
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, byte, byte>)&PCIExports.ReadConfig8);

        // Kernel_PciWriteConfig32
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x57; n[11]=0x72; n[12]=0x69; n[13]=0x74; n[14]=0x65; // PciWrite
        n[15]=0x43; n[16]=0x6F; n[17]=0x6E; n[18]=0x66; n[19]=0x69; n[20]=0x67; // Config
        n[21]=0x33; n[22]=0x32; n[23]=0; // 32
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, byte, uint, void>)&PCIExports.WriteConfig32);

        // Kernel_PciWriteConfig16
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x57; n[11]=0x72; n[12]=0x69; n[13]=0x74; n[14]=0x65;
        n[15]=0x43; n[16]=0x6F; n[17]=0x6E; n[18]=0x66; n[19]=0x69; n[20]=0x67;
        n[21]=0x31; n[22]=0x36; n[23]=0; // 16
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, byte, ushort, void>)&PCIExports.WriteConfig16);

        // Kernel_PciWriteConfig8
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x57; n[11]=0x72; n[12]=0x69; n[13]=0x74; n[14]=0x65;
        n[15]=0x43; n[16]=0x6F; n[17]=0x6E; n[18]=0x66; n[19]=0x69; n[20]=0x67;
        n[21]=0x38; n[22]=0; // 8
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, byte, byte, void>)&PCIExports.WriteConfig8);

        // Kernel_PciGetBar
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x47; n[11]=0x65; n[12]=0x74; // PciGet
        n[13]=0x42; n[14]=0x61; n[15]=0x72; n[16]=0; // Bar
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, int, uint>)&PCIExports.GetBar);

        // Kernel_PciGetBarSize
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x47; n[11]=0x65; n[12]=0x74;
        n[13]=0x42; n[14]=0x61; n[15]=0x72; n[16]=0x53; n[17]=0x69; n[18]=0x7A; n[19]=0x65; n[20]=0; // BarSize
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, int, uint>)&PCIExports.GetBarSize);

        // Kernel_PciEnableMemorySpace
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x45; n[11]=0x6E; n[12]=0x61; n[13]=0x62; n[14]=0x6C; n[15]=0x65; // PciEnable
        n[16]=0x4D; n[17]=0x65; n[18]=0x6D; n[19]=0x6F; n[20]=0x72; n[21]=0x79; // Memory
        n[22]=0x53; n[23]=0x70; n[24]=0x61; n[25]=0x63; n[26]=0x65; n[27]=0; // Space
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, void>)&PCIExports.EnableMemorySpace);

        // Kernel_PciEnableBusMaster
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F;
        n[7]=0x50; n[8]=0x63; n[9]=0x69; n[10]=0x45; n[11]=0x6E; n[12]=0x61; n[13]=0x62; n[14]=0x6C; n[15]=0x65;
        n[16]=0x42; n[17]=0x75; n[18]=0x73; n[19]=0x4D; n[20]=0x61; n[21]=0x73; n[22]=0x74; n[23]=0x65; n[24]=0x72; n[25]=0; // BusMaster
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte, byte, byte, void>)&PCIExports.EnableBusMaster);
    }

    private static void RegisterInterlockedExports()
    {
        byte* n = stackalloc byte[48];

        // Interlocked_Increment32
        // 49 6E 74 65 72 6C 6F 63 6B 65 64 5F 49 6E 63 72 65 6D 65 6E 74 33 32
        n[0]=0x49; n[1]=0x6E; n[2]=0x74; n[3]=0x65; n[4]=0x72; n[5]=0x6C; n[6]=0x6F; n[7]=0x63; n[8]=0x6B; n[9]=0x65; n[10]=0x64; n[11]=0x5F; // Interlocked_
        n[12]=0x49; n[13]=0x6E; n[14]=0x63; n[15]=0x72; n[16]=0x65; n[17]=0x6D; n[18]=0x65; n[19]=0x6E; n[20]=0x74; // Increment
        n[21]=0x33; n[22]=0x32; n[23]=0; // 32
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int*, int>)&InterlockedExports.Increment32);

        // Interlocked_Decrement32
        n[12]=0x44; n[13]=0x65; n[14]=0x63; n[15]=0x72; n[16]=0x65; n[17]=0x6D; n[18]=0x65; n[19]=0x6E; n[20]=0x74; // Decrement
        n[21]=0x33; n[22]=0x32; n[23]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int*, int>)&InterlockedExports.Decrement32);

        // Interlocked_Exchange32
        n[12]=0x45; n[13]=0x78; n[14]=0x63; n[15]=0x68; n[16]=0x61; n[17]=0x6E; n[18]=0x67; n[19]=0x65; // Exchange
        n[20]=0x33; n[21]=0x32; n[22]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int*, int, int>)&InterlockedExports.Exchange32);

        // Interlocked_CompareExchange32
        n[12]=0x43; n[13]=0x6F; n[14]=0x6D; n[15]=0x70; n[16]=0x61; n[17]=0x72; n[18]=0x65; // Compare
        n[19]=0x45; n[20]=0x78; n[21]=0x63; n[22]=0x68; n[23]=0x61; n[24]=0x6E; n[25]=0x67; n[26]=0x65; // Exchange
        n[27]=0x33; n[28]=0x32; n[29]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int*, int, int, int>)&InterlockedExports.CompareExchange32);

        // Interlocked_Add32
        n[12]=0x41; n[13]=0x64; n[14]=0x64; // Add
        n[15]=0x33; n[16]=0x32; n[17]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int*, int, int>)&InterlockedExports.Add32);

        // Interlocked_Increment64
        n[12]=0x49; n[13]=0x6E; n[14]=0x63; n[15]=0x72; n[16]=0x65; n[17]=0x6D; n[18]=0x65; n[19]=0x6E; n[20]=0x74; // Increment
        n[21]=0x36; n[22]=0x34; n[23]=0; // 64
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<long*, long>)&InterlockedExports.Increment64);

        // Interlocked_Decrement64
        n[12]=0x44; n[13]=0x65; n[14]=0x63; n[15]=0x72; n[16]=0x65; n[17]=0x6D; n[18]=0x65; n[19]=0x6E; n[20]=0x74; // Decrement
        n[21]=0x36; n[22]=0x34; n[23]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<long*, long>)&InterlockedExports.Decrement64);

        // Interlocked_Exchange64
        n[12]=0x45; n[13]=0x78; n[14]=0x63; n[15]=0x68; n[16]=0x61; n[17]=0x6E; n[18]=0x67; n[19]=0x65; // Exchange
        n[20]=0x36; n[21]=0x34; n[22]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<long*, long, long>)&InterlockedExports.Exchange64);

        // Interlocked_CompareExchange64
        n[12]=0x43; n[13]=0x6F; n[14]=0x6D; n[15]=0x70; n[16]=0x61; n[17]=0x72; n[18]=0x65; // Compare
        n[19]=0x45; n[20]=0x78; n[21]=0x63; n[22]=0x68; n[23]=0x61; n[24]=0x6E; n[25]=0x67; n[26]=0x65; // Exchange
        n[27]=0x36; n[28]=0x34; n[29]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<long*, long, long, long>)&InterlockedExports.CompareExchange64);

        // Interlocked_Add64
        n[12]=0x41; n[13]=0x64; n[14]=0x64; // Add
        n[15]=0x36; n[16]=0x34; n[17]=0;
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<long*, long, long>)&InterlockedExports.Add64);

        // Interlocked_ExchangePointer
        n[12]=0x45; n[13]=0x78; n[14]=0x63; n[15]=0x68; n[16]=0x61; n[17]=0x6E; n[18]=0x67; n[19]=0x65; // Exchange
        n[20]=0x50; n[21]=0x6F; n[22]=0x69; n[23]=0x6E; n[24]=0x74; n[25]=0x65; n[26]=0x72; n[27]=0; // Pointer
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<void**, void*, void*>)&InterlockedExports.ExchangePointer);

        // Interlocked_CompareExchangePointer
        n[12]=0x43; n[13]=0x6F; n[14]=0x6D; n[15]=0x70; n[16]=0x61; n[17]=0x72; n[18]=0x65; // Compare
        n[19]=0x45; n[20]=0x78; n[21]=0x63; n[22]=0x68; n[23]=0x61; n[24]=0x6E; n[25]=0x67; n[26]=0x65; // Exchange
        n[27]=0x50; n[28]=0x6F; n[29]=0x69; n[30]=0x6E; n[31]=0x74; n[32]=0x65; n[33]=0x72; n[34]=0; // Pointer
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<void**, void*, void*, void*>)&InterlockedExports.CompareExchangePointer);
    }

    private static void RegisterThreadExports()
    {
        byte* n = stackalloc byte[32];

        // Kernel_CreateThread
        // "Kernel_CreateThread" = 4B 65 72 6E 65 6C 5F 43 72 65 61 74 65 54 68 72 65 61 64
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x43; n[8]=0x72; n[9]=0x65; n[10]=0x61; n[11]=0x74; n[12]=0x65; // Create
        n[13]=0x54; n[14]=0x68; n[15]=0x72; n[16]=0x65; n[17]=0x61; n[18]=0x64; n[19]=0; // Thread
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<delegate* unmanaged<void*, uint>, void*, nuint, uint, uint*, ProtonOS.Threading.Thread*>)&ThreadExports.CreateThread);

        // Kernel_ExitThread
        n[7]=0x45; n[8]=0x78; n[9]=0x69; n[10]=0x74; // Exit
        n[11]=0x54; n[12]=0x68; n[13]=0x72; n[14]=0x65; n[15]=0x61; n[16]=0x64; n[17]=0; // Thread
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, void>)&ThreadExports.ExitThread);

        // Kernel_GetCurrentThreadId
        n[7]=0x47; n[8]=0x65; n[9]=0x74; // Get
        n[10]=0x43; n[11]=0x75; n[12]=0x72; n[13]=0x72; n[14]=0x65; n[15]=0x6E; n[16]=0x74; // Current
        n[17]=0x54; n[18]=0x68; n[19]=0x72; n[20]=0x65; n[21]=0x61; n[22]=0x64; // Thread
        n[23]=0x49; n[24]=0x64; n[25]=0; // Id
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint>)&ThreadExports.GetCurrentThreadId);

        // Kernel_GetCurrentThread
        n[7]=0x47; n[8]=0x65; n[9]=0x74; // Get
        n[10]=0x43; n[11]=0x75; n[12]=0x72; n[13]=0x72; n[14]=0x65; n[15]=0x6E; n[16]=0x74; // Current
        n[17]=0x54; n[18]=0x68; n[19]=0x72; n[20]=0x65; n[21]=0x61; n[22]=0x64; n[23]=0; // Thread
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ProtonOS.Threading.Thread*>)&ThreadExports.GetCurrentThread);

        // Kernel_Sleep
        n[7]=0x53; n[8]=0x6C; n[9]=0x65; n[10]=0x65; n[11]=0x70; n[12]=0; // Sleep
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, void>)&ThreadExports.Sleep);

        // Kernel_Yield
        n[7]=0x59; n[8]=0x69; n[9]=0x65; n[10]=0x6C; n[11]=0x64; n[12]=0; // Yield
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<void>)&ThreadExports.Yield);

        // Kernel_GetExitCodeThread
        n[7]=0x47; n[8]=0x65; n[9]=0x74; // Get
        n[10]=0x45; n[11]=0x78; n[12]=0x69; n[13]=0x74; // Exit
        n[14]=0x43; n[15]=0x6F; n[16]=0x64; n[17]=0x65; // Code
        n[18]=0x54; n[19]=0x68; n[20]=0x72; n[21]=0x65; n[22]=0x61; n[23]=0x64; n[24]=0; // Thread
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ProtonOS.Threading.Thread*, uint*, bool>)&ThreadExports.GetExitCodeThread);

        // Kernel_GetThreadState
        n[7]=0x47; n[8]=0x65; n[9]=0x74; // Get
        n[10]=0x54; n[11]=0x68; n[12]=0x72; n[13]=0x65; n[14]=0x61; n[15]=0x64; // Thread
        n[16]=0x53; n[17]=0x74; n[18]=0x61; n[19]=0x74; n[20]=0x65; n[21]=0; // State
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ProtonOS.Threading.Thread*, int>)&ThreadExports.GetThreadState);

        // Kernel_SuspendThread
        n[7]=0x53; n[8]=0x75; n[9]=0x73; n[10]=0x70; n[11]=0x65; n[12]=0x6E; n[13]=0x64; // Suspend
        n[14]=0x54; n[15]=0x68; n[16]=0x72; n[17]=0x65; n[18]=0x61; n[19]=0x64; n[20]=0; // Thread
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ProtonOS.Threading.Thread*, int>)&ThreadExports.SuspendThread);

        // Kernel_ResumeThread
        n[7]=0x52; n[8]=0x65; n[9]=0x73; n[10]=0x75; n[11]=0x6D; n[12]=0x65; // Resume
        n[13]=0x54; n[14]=0x68; n[15]=0x72; n[16]=0x65; n[17]=0x61; n[18]=0x64; n[19]=0; // Thread
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<ProtonOS.Threading.Thread*, int>)&ThreadExports.ResumeThread);

        // Kernel_GetThreadCount
        n[7]=0x47; n[8]=0x65; n[9]=0x74; // Get
        n[10]=0x54; n[11]=0x68; n[12]=0x72; n[13]=0x65; n[14]=0x61; n[15]=0x64; // Thread
        n[16]=0x43; n[17]=0x6F; n[18]=0x75; n[19]=0x6E; n[20]=0x74; n[21]=0; // Count
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<int>)&ThreadExports.GetThreadCount);
    }

    private static void RegisterAssemblyLoaderExports()
    {
        byte* n = stackalloc byte[32];

        // Kernel_LoadOwnedAssembly
        // "Kernel_LoadOwnedAssembly" = 4B 65 72 6E 65 6C 5F 4C 6F 61 64 4F 77 6E 65 64 41 73 73 65 6D 62 6C 79
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x4C; n[8]=0x6F; n[9]=0x61; n[10]=0x64; // Load
        n[11]=0x4F; n[12]=0x77; n[13]=0x6E; n[14]=0x65; n[15]=0x64; // Owned
        n[16]=0x41; n[17]=0x73; n[18]=0x73; n[19]=0x65; n[20]=0x6D; n[21]=0x62; n[22]=0x6C; n[23]=0x79; n[24]=0; // Assembly
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<byte*, ulong, uint, uint>)&AssemblyLoaderExports.LoadOwnedAssembly);

        // Kernel_FindDriverEntryType
        // "Kernel_FindDriverEntryType" = 4B 65 72 6E 65 6C 5F 46 69 6E 64 44 72 69 76 65 72 45 6E 74 72 79 54 79 70 65
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x46; n[8]=0x69; n[9]=0x6E; n[10]=0x64; // Find
        n[11]=0x44; n[12]=0x72; n[13]=0x69; n[14]=0x76; n[15]=0x65; n[16]=0x72; // Driver
        n[17]=0x45; n[18]=0x6E; n[19]=0x74; n[20]=0x72; n[21]=0x79; // Entry
        n[22]=0x54; n[23]=0x79; n[24]=0x70; n[25]=0x65; n[26]=0; // Type
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, uint>)&AssemblyLoaderExports.FindDriverEntryType);

        // Kernel_FindMethodByName
        // "Kernel_FindMethodByName" = 4B 65 72 6E 65 6C 5F 46 69 6E 64 4D 65 74 68 6F 64 42 79 4E 61 6D 65
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x46; n[8]=0x69; n[9]=0x6E; n[10]=0x64; // Find
        n[11]=0x4D; n[12]=0x65; n[13]=0x74; n[14]=0x68; n[15]=0x6F; n[16]=0x64; // Method
        n[17]=0x42; n[18]=0x79; // By
        n[19]=0x4E; n[20]=0x61; n[21]=0x6D; n[22]=0x65; n[23]=0; // Name
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, uint, byte*, uint>)&AssemblyLoaderExports.FindMethodByName);

        // Kernel_JitAndCallInit
        // "Kernel_JitAndCallInit" = 4B 65 72 6E 65 6C 5F 4A 69 74 41 6E 64 43 61 6C 6C 49 6E 69 74
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x4A; n[8]=0x69; n[9]=0x74; // Jit
        n[10]=0x41; n[11]=0x6E; n[12]=0x64; // And
        n[13]=0x43; n[14]=0x61; n[15]=0x6C; n[16]=0x6C; // Call
        n[17]=0x49; n[18]=0x6E; n[19]=0x69; n[20]=0x74; n[21]=0; // Init
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, uint, bool>)&AssemblyLoaderExports.JitAndCallInit);

        // Kernel_JitAndCallShutdown
        // "Kernel_JitAndCallShutdown" = 4B 65 72 6E 65 6C 5F 4A 69 74 41 6E 64 43 61 6C 6C 53 68 75 74 64 6F 77 6E
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x4A; n[8]=0x69; n[9]=0x74; // Jit
        n[10]=0x41; n[11]=0x6E; n[12]=0x64; // And
        n[13]=0x43; n[14]=0x61; n[15]=0x6C; n[16]=0x6C; // Call
        n[17]=0x53; n[18]=0x68; n[19]=0x75; n[20]=0x74; n[21]=0x64; n[22]=0x6F; n[23]=0x77; n[24]=0x6E; n[25]=0; // Shutdown
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, uint, void>)&AssemblyLoaderExports.JitAndCallShutdown);

        // Kernel_UnloadContext
        // "Kernel_UnloadContext" = 4B 65 72 6E 65 6C 5F 55 6E 6C 6F 61 64 43 6F 6E 74 65 78 74
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x55; n[8]=0x6E; n[9]=0x6C; n[10]=0x6F; n[11]=0x61; n[12]=0x64; // Unload
        n[13]=0x43; n[14]=0x6F; n[15]=0x6E; n[16]=0x74; n[17]=0x65; n[18]=0x78; n[19]=0x74; n[20]=0; // Context
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint, int>)&AssemblyLoaderExports.UnloadContext);

        // Kernel_CreateContext
        // "Kernel_CreateContext" = 4B 65 72 6E 65 6C 5F 43 72 65 61 74 65 43 6F 6E 74 65 78 74
        n[0]=0x4B; n[1]=0x65; n[2]=0x72; n[3]=0x6E; n[4]=0x65; n[5]=0x6C; n[6]=0x5F; // Kernel_
        n[7]=0x43; n[8]=0x72; n[9]=0x65; n[10]=0x61; n[11]=0x74; n[12]=0x65; // Create
        n[13]=0x43; n[14]=0x6F; n[15]=0x6E; n[16]=0x74; n[17]=0x65; n[18]=0x78; n[19]=0x74; n[20]=0; // Context
        KernelExportRegistry.Register(n, (void*)(delegate* unmanaged<uint>)&AssemblyLoaderExports.CreateContext);
    }
}
