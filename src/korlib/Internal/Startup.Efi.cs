// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#if UEFI

using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Internal.Runtime.CompilerHelpers
{
    unsafe partial class StartupCodeHelpers
    {
        [RuntimeImport("*", "__managed__Main")]
        [MethodImpl(MethodImplOptions.InternalCall)]
        static extern int ManagedMain(int argc, char** argv);

        // Fixed memory addresses for GDB debug marker (used to find kernel load address)
        // GDB can watch 0x10000 for the marker 0xDEADBEEF, then read ImageBase from 0x10008
        private const ulong GDB_DEBUG_MARKER_ADDR = 0x10000;
        private const ulong GDB_DEBUG_IMAGEBASE_ADDR = 0x10008;
        private const ulong GDB_DEBUG_MARKER_VALUE = 0xDEADBEEF;

        [RuntimeExport("EfiMain")]
        static long EfiMain(IntPtr imageHandle, EFI_SYSTEM_TABLE* systemTable)
        {
            // First thing: write the kernel's load address for GDB debugging
            // This must happen before ANY other code runs so GDB can catch it early
            WriteGdbDebugMarker(imageHandle, systemTable);

            SetEfiSystemTable(systemTable);
            ManagedMain(0, null);

            while (true) ;
        }

        static void WriteGdbDebugMarker(IntPtr imageHandle, EFI_SYSTEM_TABLE* systemTable)
        {
            // Get the EFI_LOADED_IMAGE_PROTOCOL to find our actual load address
            EFI_GUID guid;
            EFI_GUID.InitLoadedImageProtocol(&guid);

            EFI_LOADED_IMAGE_PROTOCOL* loadedImage = null;
            ulong status = systemTable->BootServices->HandleProtocol(
                (nint)imageHandle,
                &guid,
                (void**)&loadedImage);

            // Write the ImageBase to known address (0x10008)
            // Write magic marker to 0x10000 to signal GDB
            // Use direct pointer writes - they won't be optimized away at this early stage
            ulong* markerPtr = (ulong*)GDB_DEBUG_MARKER_ADDR;
            ulong* imageBasePtr = (ulong*)GDB_DEBUG_IMAGEBASE_ADDR;

            if (status == 0 && loadedImage != null)
            {
                *imageBasePtr = (ulong)loadedImage->ImageBase;
            }
            else
            {
                *imageBasePtr = 0; // Signal error
            }

            // Write marker last - GDB watches for this
            *markerPtr = GDB_DEBUG_MARKER_VALUE;
        }

        internal static unsafe void InitializeCommandLineArgsW(int argc, char** argv)
        {
            // argc and argv are garbage because EfiMain didn't pass any
        }

        internal static string[] GetMainMethodArguments()
        {
            return new string[0];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EFI_HANDLE
    {
        private IntPtr _handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL
    {
        private readonly IntPtr _pad0;
        public readonly delegate* unmanaged<void*, char*, void*> OutputString;
        private readonly IntPtr _pad1;
        private readonly IntPtr _pad2;
        private readonly IntPtr _pad3;
        public readonly delegate* unmanaged<void*, uint, void> SetAttribute;
        private readonly IntPtr _pad4;
        public readonly delegate* unmanaged<void*, uint, uint, void> SetCursorPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct EFI_INPUT_KEY
    {
        public readonly ushort ScanCode;
        public readonly ushort UnicodeChar;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct EFI_SIMPLE_TEXT_INPUT_PROTOCOL
    {
        private readonly IntPtr _pad0;
        public readonly delegate* unmanaged<void*, EFI_INPUT_KEY*, ulong> ReadKeyStroke;
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct EFI_TABLE_HEADER
    {
        public readonly ulong Signature;
        public readonly uint Revision;
        public readonly uint HeaderSize;
        public readonly uint Crc32;
        public readonly uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct EFI_SYSTEM_TABLE
    {
        public readonly EFI_TABLE_HEADER Hdr;
        public readonly char* FirmwareVendor;
        public readonly uint FirmwareRevision;
        public readonly EFI_HANDLE ConsoleInHandle;
        public readonly EFI_SIMPLE_TEXT_INPUT_PROTOCOL* ConIn;
        public readonly EFI_HANDLE ConsoleOutHandle;
        public readonly EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL* ConOut;
        public readonly EFI_HANDLE StandardErrorHandle;
        public readonly EFI_SIMPLE_TEXT_OUTPUT_PROTOCOL* StdErr;
        public readonly EFI_RUNTIME_SERVICES* RuntimeServices;
        public readonly EFI_BOOT_SERVICES* BootServices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EFI_TIME
    {
        public ushort Year;
        public byte Month;
        public byte Day;
        public byte Hour;
        public byte Minute;
        public byte Second;
        public byte Pad1;
        public uint Nanosecond;
        public short TimeZone;
        public byte Daylight;
        public byte PAD2;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EFI_TIME_CAPABILITIES
    {
        public uint Resolution;
        public uint Accuracy;
        public byte SetsToZero;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct EFI_RUNTIME_SERVICES
    {
        public readonly EFI_TABLE_HEADER Hdr;
        public readonly delegate* unmanaged<EFI_TIME*, EFI_TIME_CAPABILITIES*, ulong> GetTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct EFI_BOOT_SERVICES
    {
        readonly EFI_TABLE_HEADER Hdr;
        private readonly void* RaiseTPL;           // 0
        private readonly void* RestoreTPL;         // 1
        private readonly void* AllocatePages;      // 2
        private readonly void* FreePages;          // 3
        private readonly void* GetMemoryMap;       // 4
        public readonly delegate* unmanaged<int, nint, void**, ulong> AllocatePool; // 5
        private readonly void* FreePool;           // 6
        private readonly void* CreateEvent;        // 7
        private readonly void* SetTimer;           // 8
        private readonly void* WaitForEvent;       // 9
        private readonly void* SignalEvent;        // 10
        private readonly void* CloseEvent;         // 11
        private readonly void* CheckEvent;         // 12
        private readonly void* InstallProtocolInterface;    // 13
        private readonly void* ReinstallProtocolInterface;  // 14
        private readonly void* UninstallProtocolInterface;  // 15
        public readonly delegate* unmanaged<nint, EFI_GUID*, void**, ulong> HandleProtocol; // 16
        private readonly void* Reserved;           // 17
        private readonly void* RegisterProtocolNotify;      // 18
        private readonly void* LocateHandle;       // 19
        private readonly void* LocateDevicePath;   // 20
        private readonly void* InstallConfigurationTable;   // 21
        private readonly void* LoadImage;          // 22
        private readonly void* StartImage;         // 23
        private readonly void* Exit;               // 24
        private readonly void* UnloadImage;        // 25
        private readonly void* ExitBootServices;   // 26
        private readonly void* GetNextMonotonicCount;       // 27
        public readonly delegate* unmanaged<uint, ulong> Stall; // 28
    }

    [StructLayout(LayoutKind.Sequential)]
    struct EFI_GUID
    {
        public uint Data1;
        public ushort Data2;
        public ushort Data3;
        public unsafe fixed byte Data4[8];

        public static EFI_GUID LoadedImageProtocol => new EFI_GUID
        {
            Data1 = 0x5B1B31A1,
            Data2 = 0x9562,
            Data3 = 0x11d2,
            // Data4 = { 0x8E, 0x3F, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B }
        };

        public static unsafe void InitLoadedImageProtocol(EFI_GUID* guid)
        {
            guid->Data1 = 0x5B1B31A1;
            guid->Data2 = 0x9562;
            guid->Data3 = 0x11d2;
            guid->Data4[0] = 0x8E;
            guid->Data4[1] = 0x3F;
            guid->Data4[2] = 0x00;
            guid->Data4[3] = 0xA0;
            guid->Data4[4] = 0xC9;
            guid->Data4[5] = 0x69;
            guid->Data4[6] = 0x72;
            guid->Data4[7] = 0x3B;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe readonly struct EFI_LOADED_IMAGE_PROTOCOL
    {
        public readonly uint Revision;
        public readonly nint ParentHandle;
        public readonly EFI_SYSTEM_TABLE* SystemTable;
        public readonly nint DeviceHandle;
        public readonly void* FilePath;
        public readonly void* Reserved;
        public readonly uint LoadOptionsSize;
        public readonly void* LoadOptions;
        public readonly void* ImageBase;          // The base address at which the image was loaded
        public readonly ulong ImageSize;
        public readonly int ImageCodeType;
        public readonly int ImageDataType;
        public readonly void* Unload;
    }
}

#endif
