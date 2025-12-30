; ProtonOS UEFI Bootloader
; Loads KERNEL.BIN at a predictable address (32MB, 64MB, or 128MB)
; This allows GDB to reliably find and debug the kernel.
;
; The kernel is a PE/COFF binary but NOT an EFI application - this loader
; handles relocations and jumps to the kernel entry point directly.
;
; Current state: Passes UEFI ImageHandle/SystemTable to kernel, which then
; handles file loading and ExitBootServices. Future work will move all
; UEFI interaction to this bootloader and pass a BootInfo structure instead.
;
; Build: nasm -f win64 boot.asm -o boot.obj
; Link:  lld-link -subsystem:efi_application -entry:EfiMain -out:BOOTX64.EFI boot.obj

BITS 64
DEFAULT REL

;; ============================================================================
;; PE/COFF Header Structures (offsets)
;; ============================================================================

; DOS Header
DOS_MAGIC           equ 0x5A4D      ; 'MZ'
DOS_LFANEW          equ 0x3C        ; Offset to PE signature

; PE Signature
PE_SIGNATURE        equ 0x00004550  ; 'PE\0\0'

; COFF Header (immediately after PE signature)
COFF_MACHINE        equ 0           ; +0: Machine type
COFF_NUM_SECTIONS   equ 2           ; +2: Number of sections
COFF_SIZE_OPT_HDR   equ 16          ; +16: Size of optional header
COFF_HEADER_SIZE    equ 20          ; Size of COFF header

; PE32+ Optional Header
OPT_MAGIC           equ 0           ; +0: Magic (0x20B for PE32+)
OPT_ENTRY_POINT     equ 16          ; +16: AddressOfEntryPoint
OPT_IMAGE_BASE      equ 24          ; +24: ImageBase (64-bit)
OPT_SECTION_ALIGN   equ 32          ; +32: SectionAlignment
OPT_FILE_ALIGN      equ 36          ; +36: FileAlignment
OPT_SIZE_IMAGE      equ 56          ; +56: SizeOfImage
OPT_SIZE_HEADERS    equ 60          ; +60: SizeOfHeaders
OPT_NUM_RVA_SIZES   equ 108         ; +108: NumberOfRvaAndSizes
OPT_DATA_DIR        equ 112         ; +112: Data Directory start

; Data Directory indices
DATA_DIR_BASERELOC  equ 5           ; Base Relocation Table

; Section Header
SEC_NAME            equ 0           ; +0: Name (8 bytes)
SEC_VIRT_SIZE       equ 8           ; +8: VirtualSize
SEC_VIRT_ADDR       equ 12          ; +12: VirtualAddress (RVA)
SEC_RAW_SIZE        equ 16          ; +16: SizeOfRawData
SEC_RAW_PTR         equ 20          ; +20: PointerToRawData
SEC_HEADER_SIZE     equ 40          ; Size of section header

; Base Relocation Block
RELOC_PAGE_RVA      equ 0           ; +0: PageRVA
RELOC_BLOCK_SIZE    equ 4           ; +4: BlockSize
RELOC_ENTRIES       equ 8           ; +8: Entries start

; Relocation types (high 4 bits of entry)
RELOC_ABSOLUTE      equ 0           ; No relocation
RELOC_DIR64         equ 10          ; 64-bit address

;; ============================================================================
;; UEFI Constants
;; ============================================================================

EFI_SUCCESS                     equ 0
EFI_LOAD_ERROR                  equ 1
EFI_NOT_FOUND                   equ 14

; Memory types
EFI_LOADER_DATA                 equ 2

; AllocateType
ALLOCATE_ADDRESS                equ 2   ; Allocate at specific address

; Open modes
EFI_FILE_MODE_READ              equ 0x0000000000000001

; EFI_BOOT_SERVICES offsets (after 24-byte header)
BS_ALLOCATE_PAGES               equ 40
BS_FREE_PAGES                   equ 48
BS_GET_MEMORY_MAP               equ 56
BS_ALLOCATE_POOL                equ 64
BS_FREE_POOL                    equ 72
BS_HANDLE_PROTOCOL              equ 152

; UEFI Protocol GUIDs
; EFI_LOADED_IMAGE_PROTOCOL_GUID = {5B1B31A1-9562-11D2-8E3F-00A0C969723B}
; EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_GUID = {964E5B22-6459-11D2-8E39-00A0C969723B}

;; ============================================================================
;; Memory Layout (fixed addresses)
;; ============================================================================
;;
;; 0x00100000 (1 MB)   - BootInfo structure (1 MB reserved)
;; 0x02000000 (32 MB)  - UEFI FS files copied here (64 MB reserved)
;; 0x08000000 (128 MB) - Kernel (8 MB reserved, fixed location)
;;
;; This layout ensures all critical data is in low memory that won't be
;; reclaimed after ExitBootServices. The bootloader copies all UEFI FS
;; files to the 32MB region before exiting boot services.
;;
;; ============================================================================

; BootInfo location (1 MB)
BOOTINFO_ADDR       equ 0x00100000
BOOTINFO_RESERVED   equ 0x00100000  ; 1 MB reserved for BootInfo + file table

; File storage area (32 MB, 64 MB reserved)
FILES_ADDR          equ 0x02000000  ; 32 MB
FILES_RESERVED      equ 0x04000000  ; 64 MB reserved for files

; Kernel load address (128 MB, fixed - no fallbacks)
KERNEL_ADDR         equ 0x08000000  ; 128 MB
MAX_KERNEL_SIZE     equ 0x00800000  ; 8 MB reserved

; Maximum files we can track (each LoadedFile entry is 88 bytes)
MAX_LOADED_FILES    equ 256

;; ============================================================================
;; BootInfo Constants (must match BootInfo.cs)
;; ============================================================================

BOOTINFO_MAGIC      equ 0x50524F544F4E4F53  ; "PROTONOS"
BOOTINFO_VERSION    equ 2                    ; Version 2: bootloader handles ExitBootServices
BOOTINFO_SIZE       equ 256                  ; Size of BootInfo struct (fixed header)

; LoadedFile entry size (must match BootInfo.cs)
; PhysicalAddress(8) + Size(8) + NameBytes[64] + Flags(4) + Reserved(4) = 88 bytes
LOADED_FILE_SIZE    equ 88
LF_PHYS_ADDR        equ 0
LF_SIZE             equ 8
LF_NAME             equ 16
LF_NAME_LEN         equ 64
LF_FLAGS            equ 80
LF_RESERVED         equ 84

; BootInfo offsets
BI_MAGIC            equ 0
BI_VERSION          equ 8
BI_FLAGS            equ 12
BI_MEMMAP_ADDR      equ 16
BI_MEMMAP_ENTRIES   equ 24
BI_MEMMAP_ENTRYSIZE equ 28
BI_KERNEL_PHYS      equ 32
BI_KERNEL_VIRT      equ 40
BI_KERNEL_SIZE      equ 48
BI_KERNEL_ENTRY     equ 56
BI_FILES_ADDR       equ 64
BI_FILES_COUNT      equ 72
BI_ACPI_RSDP        equ 80
BI_FB_ADDR          equ 88
BI_FB_WIDTH         equ 96
BI_FB_HEIGHT        equ 100
BI_FB_PITCH         equ 104
BI_FB_BPP           equ 108
BI_SERIAL_PORT      equ 112
; Offsets 120-135: Reserved[8]

; BootInfo flags
BIF_HAS_FRAMEBUFFER equ 0x01
BIF_HAS_ACPI        equ 0x02
BIF_HAS_SERIAL      equ 0x04

; MemoryMapEntry offsets (16 bytes each)
MME_PHYS_START      equ 0
MME_PHYS_END        equ 8
MME_TYPE            equ 16
MME_FLAGS           equ 20
MME_SIZE            equ 24

; MemoryType values
MT_AVAILABLE        equ 0
MT_RESERVED         equ 1
MT_ACPI_RECLAIM     equ 2
MT_ACPI_NVS         equ 3
MT_KERNEL           equ 4
MT_LOADED_FILE      equ 5
MT_BOOTINFO         equ 6
MT_PAGETABLES       equ 7
MT_STACK            equ 8

; Memory map limits
MAX_MEMMAP_ENTRIES  equ 256
MEMMAP_BUFFER_SIZE  equ 8192

; ACPI GUIDs
; EFI_ACPI_20_TABLE_GUID = {8868E871-E4F1-11D3-BC22-0080C73C8881}
; EFI_ACPI_TABLE_GUID = {EB9D2D30-2D88-11D3-9A16-0090273FC14D}

;; ============================================================================
;; Data Section
;; ============================================================================

section .data

; UEFI handles/pointers
ImageHandle:        dq 0
SystemTable:        dq 0
BootServices:       dq 0
ConOut:             dq 0

; File system handles
LoadedImage:        dq 0
FileSystem:         dq 0
RootDir:            dq 0
KernelFile:         dq 0

; Fixed memory regions (allocated at specific addresses)
BootInfoBase:       dq 0            ; BootInfo at 1 MB
FilesBase:          dq 0            ; Files at 32 MB
FilesNextAddr:      dq 0            ; Next address to copy file to
FilesCount:         dq 0            ; Number of files copied

; Kernel load info
KernelBuffer:       dq 0            ; Address where kernel file was loaded (UEFI temp)
KernelFileSize:     dq 0            ; Size of kernel file
KernelBase:         dq 0            ; Final kernel base address (128 MB)
KernelEntry:        dq 0            ; Kernel entry point address
KernelImageSize:    dq 0            ; SizeOfImage from PE header

; Strings (UCS-2 for UEFI)
MsgLoading:         dw 'P','r','o','t','o','n','O','S',' ','B','o','o','t','l','o','a','d','e','r',13,10,0
MsgLoadingKernel:   dw 'L','o','a','d','i','n','g',' ','k','e','r','n','e','l','.','.','.',13,10,0
MsgRelocating:      dw 'R','e','l','o','c','a','t','i','n','g',' ','k','e','r','n','e','l','.','.','.',13,10,0
MsgStarting:        dw 'S','t','a','r','t','i','n','g',' ','k','e','r','n','e','l','.','.','.',13,10,0
MsgError:           dw 'E','R','R','O','R',':',' ',0
MsgAllocFailed:     dw 'M','e','m','o','r','y',' ','a','l','l','o','c','a','t','i','o','n',' ','f','a','i','l','e','d',13,10,0
MsgFileFailed:      dw 'F','i','l','e',' ','l','o','a','d',' ','f','a','i','l','e','d',13,10,0
MsgBadPE:           dw 'I','n','v','a','l','i','d',' ','P','E',' ','f','o','r','m','a','t',13,10,0
MsgNewline:         dw 13,10,0

; Kernel filename (UCS-2)
KernelPath:         dw '\','E','F','I','\','B','O','O','T','\','K','E','R','N','E','L','.','B','I','N',0

; EFI_LOADED_IMAGE_PROTOCOL_GUID
LoadedImageGuid:
    dd 0x5B1B31A1
    dw 0x9562
    dw 0x11D2
    db 0x8E, 0x3F, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B

; EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_GUID
FileSystemGuid:
    dd 0x0964E5B22
    dw 0x6459
    dw 0x11D2
    db 0x8E, 0x39, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B

; EFI_FILE_INFO_GUID
FileInfoGuid:
    dd 0x09576E92
    dw 0x6D3F
    dw 0x11D2
    db 0x8E, 0x39, 0x00, 0xA0, 0xC9, 0x69, 0x72, 0x3B

; Buffer for file info
align 8
FileInfoBuffer:     times 256 db 0
FileInfoSize:       dq 256

; BootInfo structure (will be passed to kernel)
align 8
BootInfo:           times BOOTINFO_SIZE db 0
BootInfoPtr:        dq 0                    ; Pointer to BootInfo (for kernel)

; Memory map buffers
align 8
UefiMemMapBuffer:   times MEMMAP_BUFFER_SIZE db 0   ; Raw UEFI memory map
UefiMemMapSize:     dq MEMMAP_BUFFER_SIZE
UefiMemMapKey:      dq 0
UefiDescSize:       dq 0
UefiDescVersion:    dq 0

; Converted memory map (our format)
align 8
MemMapBuffer:       times (MAX_MEMMAP_ENTRIES * MME_SIZE) db 0
MemMapEntries:      dq 0

; ACPI RSDP address
AcpiRsdp:           dq 0

; ACPI 2.0 GUID: {8868E871-E4F1-11D3-BC22-0080C73C8881}
Acpi20Guid:
    dd 0x8868E871
    dw 0xE4F1
    dw 0x11D3
    db 0xBC, 0x22, 0x00, 0x80, 0xC7, 0x3C, 0x88, 0x81

; ACPI 1.0 GUID: {EB9D2D30-2D88-11D3-9A16-0090273FC14D}
Acpi10Guid:
    dd 0xEB9D2D30
    dw 0x2D88
    dw 0x11D3
    db 0x9A, 0x16, 0x00, 0x90, 0x27, 0x3F, 0xC1, 0x4D

; Exit boot services message
MsgExiting:         dw 'E','x','i','t','i','n','g',' ','b','o','o','t',' ','s','e','r','v','i','c','e','s','.','.','.',13,10,0
MsgExitFailed:      dw 'E','x','i','t','B','o','o','t','S','e','r','v','i','c','e','s',' ','f','a','i','l','e','d',13,10,0
MsgCopyingFiles:    dw 'C','o','p','y','i','n','g',' ','f','i','l','e','s','.','.','.',13,10,0

; EFI_FILE_PROTOCOL offsets
EFI_FILE_OPEN       equ 8
EFI_FILE_CLOSE      equ 16
EFI_FILE_READ       equ 32
EFI_FILE_SET_POS    equ 56
EFI_FILE_GET_INFO   equ 64

; EFI_FILE_INFO offsets
; EFI_TIME is 16 bytes: Year(2)+Month(1)+Day(1)+Hour(1)+Minute(1)+Second(1)+Pad1(1)+Nanosecond(4)+TimeZone(2)+Daylight(1)+Pad2(1)
FI_SIZE             equ 0       ; Size of structure including filename
FI_FILE_SIZE        equ 8       ; Logical file size
FI_PHYS_SIZE        equ 16      ; Physical size
; CreateTime at 24 (16 bytes), LastAccessTime at 40 (16 bytes), ModificationTime at 56 (16 bytes)
FI_ATTRIBUTE        equ 72      ; Attributes (at offset 24 + 48 = 72)
FI_FILENAME         equ 80      ; Filename starts here (variable length UCS-2)
FI_HEADER_SIZE      equ 80      ; Fixed header before filename

; EFI file attributes
EFI_FILE_DIRECTORY  equ 0x10

; Buffer for file info when enumerating
align 8
EnumBuffer:         times 512 db 0      ; Buffer for EFI_FILE_INFO during enumeration
EnumBufferSize:     dq 512

; Temporary file handle during enumeration
TempFileHandle:     dq 0

; Current directory depth (for path building)
DirDepth:           dq 0

; File table is stored right after BootInfo header (at BootInfoBase + BOOTINFO_SIZE)
; Each entry is LOADED_FILE_SIZE bytes

;; ============================================================================
;; Code Section
;; ============================================================================

section .text

;; ============================================================================
;; Debug: Print character to serial port
;; ============================================================================
SerialPutChar:
    push rdx
    mov dx, 0x3F8
    out dx, al
    pop rdx
    ret

;; Print hex digit (low nibble of al)
SerialPutHex4:
    and al, 0x0F
    cmp al, 10
    jb .digit
    add al, 'A' - 10
    jmp SerialPutChar
.digit:
    add al, '0'
    jmp SerialPutChar

;; Print 64-bit hex value in rax
SerialPutHex64:
    push rcx
    push rax
    mov rcx, 16
.loop:
    rol rax, 4
    push rax
    call SerialPutHex4
    pop rax
    dec rcx
    jnz .loop
    pop rax
    pop rcx
    ret

; EFI_STATUS EFIAPI EfiMain(EFI_HANDLE ImageHandle, EFI_SYSTEM_TABLE* SystemTable)
global EfiMain
EfiMain:
    ; Debug: Print startup marker
    mov al, 'B'
    call SerialPutChar
    mov al, '>'
    call SerialPutChar

    ; Save arguments
    mov [rel ImageHandle], rcx
    mov [rel SystemTable], rdx

    ; Get BootServices and ConOut from SystemTable
    mov rax, [rdx + 64]             ; SystemTable->BootServices
    mov [rel BootServices], rax
    mov rax, [rdx + 64]             ; SystemTable->ConOut (offset 64)
    ; Actually ConOut is at offset 64, BootServices at 96 for newer UEFI
    ; Let me use the correct offsets:
    ; SystemTable layout:
    ;   +0:  Hdr (24 bytes)
    ;   +24: FirmwareVendor
    ;   +32: FirmwareRevision
    ;   +40: ConsoleInHandle
    ;   +48: ConIn
    ;   +56: ConsoleOutHandle
    ;   +64: ConOut
    ;   +72: StandardErrorHandle
    ;   +80: StdErr
    ;   +88: RuntimeServices
    ;   +96: BootServices
    mov rax, [rdx + 64]             ; ConOut
    mov [rel ConOut], rax
    mov rax, [rdx + 96]             ; BootServices
    mov [rel BootServices], rax

    ; Debug: Show we got this far
    mov al, '1'
    call SerialPutChar

    ; Print banner
    lea rcx, [rel MsgLoading]
    call PrintString

    ; Debug
    mov al, '2'
    call SerialPutChar

    ; === Allocate fixed memory regions ===
    ; These allocations establish our memory layout before loading anything.
    ; All critical data will be in these regions, which persist after ExitBootServices.

    ; Allocate BootInfo region at 1 MB (256 pages = 1 MB)
    mov qword [rel BootInfoBase], BOOTINFO_ADDR
    mov rcx, ALLOCATE_ADDRESS
    mov rdx, EFI_LOADER_DATA
    mov r8, (BOOTINFO_RESERVED / 4096)  ; Pages
    lea r9, [rel BootInfoBase]
    sub rsp, 32
    mov rax, [rel BootServices]
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32
    test rax, rax
    jnz .error_bootinfo_alloc

    ; Allocate Files region at 32 MB (16384 pages = 64 MB)
    mov qword [rel FilesBase], FILES_ADDR
    mov rcx, ALLOCATE_ADDRESS
    mov rdx, EFI_LOADER_DATA
    mov r8, (FILES_RESERVED / 4096)     ; Pages
    lea r9, [rel FilesBase]
    sub rsp, 32
    mov rax, [rel BootServices]
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32
    test rax, rax
    jnz .error_files_alloc

    ; Allocate Kernel region at 128 MB (2048 pages = 8 MB)
    mov qword [rel KernelBase], KERNEL_ADDR
    mov rcx, ALLOCATE_ADDRESS
    mov rdx, EFI_LOADER_DATA
    mov r8, (MAX_KERNEL_SIZE / 4096)    ; Pages
    lea r9, [rel KernelBase]
    sub rsp, 32
    mov rax, [rel BootServices]
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32
    test rax, rax
    jnz .error_kernel_alloc

    mov al, 'M'
    call SerialPutChar
    mov al, '!'
    call SerialPutChar

    ; Get LoadedImage protocol (to find our device)
    ; HandleProtocol(Handle, &GUID, &Interface) - 3 params
    mov rcx, [rel ImageHandle]
    lea rdx, [rel LoadedImageGuid]
    lea r8, [rel LoadedImage]       ; Output: interface pointer
    mov rax, [rel BootServices]
    sub rsp, 32
    call [rax + BS_HANDLE_PROTOCOL]
    add rsp, 32
    test rax, rax
    jnz .error_li

    mov al, '3'
    call SerialPutChar

    ; Get SimpleFileSystem protocol from our device
    ; HandleProtocol(Handle, &GUID, &Interface) - 3 params
    mov rax, [rel LoadedImage]
    mov rcx, [rax + 24]             ; DeviceHandle (offset 24 in LoadedImage)
    lea rdx, [rel FileSystemGuid]
    lea r8, [rel FileSystem]        ; Output: interface pointer
    mov rax, [rel BootServices]
    sub rsp, 32
    call [rax + BS_HANDLE_PROTOCOL]
    add rsp, 32
    test rax, rax
    jnz .error_fs

    mov al, '4'
    call SerialPutChar

    ; Open root directory
    ; EFI_SIMPLE_FILE_SYSTEM_PROTOCOL: Revision=0, OpenVolume=8
    mov rcx, [rel FileSystem]
    lea rdx, [rel RootDir]
    sub rsp, 32
    call [rcx + 8]                  ; OpenVolume(FileSystem, &Root)
    add rsp, 32
    test rax, rax
    jnz .error_file

    mov al, '5'
    call SerialPutChar

    ; Open kernel file
    lea rcx, [rel MsgLoadingKernel]
    call PrintString

    ; EFI_FILE_PROTOCOL: Revision=0, Open=8, Close=16, Delete=24, Read=32, ...
    mov rcx, [rel RootDir]
    lea rdx, [rel KernelFile]
    lea r8, [rel KernelPath]
    mov r9, EFI_FILE_MODE_READ
    xor rax, rax
    push rax                        ; Attributes = 0
    sub rsp, 32                     ; Shadow space
    call [rcx + 8]                  ; Open(RootDir, &File, Path, Mode, Attr)
    add rsp, 40
    test rax, rax
    jnz .error_file

    mov al, '6'
    call SerialPutChar

    ; Get file size
    ; EFI_FILE_PROTOCOL: GetInfo=64
    mov rcx, [rel KernelFile]
    lea rdx, [rel FileInfoGuid]
    lea r8, [rel FileInfoSize]
    lea r9, [rel FileInfoBuffer]
    sub rsp, 32
    call [rcx + 64]                 ; GetInfo(File, &InfoType, &Size, Buffer)
    add rsp, 32
    test rax, rax
    jnz .error_file

    mov al, '7'
    call SerialPutChar

    ; FileInfo->FileSize is at offset 8
    mov rax, [rel FileInfoBuffer + 8]
    mov [rel KernelFileSize], rax

    ; Allocate buffer for kernel file (in any available memory)
    mov rcx, 0                      ; AllocateAnyPages
    mov rdx, EFI_LOADER_DATA
    mov r8, rax
    add r8, 0xFFF
    shr r8, 12                      ; Pages needed
    lea r9, [rel KernelBuffer]
    mov rax, [rel BootServices]
    sub rsp, 32
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32
    test rax, rax
    jnz .error_alloc

    mov al, '8'
    call SerialPutChar

    ; Read kernel file into buffer
    ; EFI_FILE_PROTOCOL: Read(This, &BufferSize, Buffer) - offset 32
    mov rcx, [rel KernelFile]
    lea rdx, [rel KernelFileSize]   ; &size (in: bytes to read, out: bytes read)
    mov r8, [rel KernelBuffer]      ; buffer address
    sub rsp, 32
    call [rcx + 32]
    add rsp, 32
    test rax, rax
    jnz .error_file

    mov al, '9'
    call SerialPutChar

    ; Close kernel file
    ; EFI_FILE_PROTOCOL: Close=16
    mov rcx, [rel KernelFile]
    sub rsp, 32
    call [rcx + 16]                 ; Close(File)
    add rsp, 32

    ; Debug: print file size and first bytes
    mov al, 'S'
    call SerialPutChar
    mov rax, [rel KernelFileSize]
    call SerialPutHex64
    mov al, ':'
    call SerialPutChar

    ; Verify PE signature
    mov rsi, [rel KernelBuffer]

    ; Debug: print first 4 bytes
    movzx eax, byte [rsi]
    call SerialPutHex4
    call SerialPutHex4
    movzx eax, byte [rsi+1]
    call SerialPutHex4
    call SerialPutHex4
    mov al, 10
    call SerialPutChar

    movzx eax, word [rsi]
    cmp ax, DOS_MAGIC               ; 'MZ'
    jne .error_pe

    ; Get PE header offset
    mov eax, [rsi + DOS_LFANEW]
    add rsi, rax                    ; RSI now points to PE signature
    cmp dword [rsi], PE_SIGNATURE
    jne .error_pe

    ; Skip PE signature (4 bytes) to COFF header
    add rsi, 4

    ; Get number of sections and optional header size
    movzx ecx, word [rsi + COFF_NUM_SECTIONS]
    movzx edx, word [rsi + COFF_SIZE_OPT_HDR]

    ; Save for later
    push rcx                        ; Number of sections
    push rdx                        ; Optional header size

    ; Point to optional header
    add rsi, COFF_HEADER_SIZE

    ; Verify PE32+ magic
    movzx eax, word [rsi + OPT_MAGIC]
    cmp ax, 0x20B                   ; PE32+
    jne .error_pe

    ; Get kernel info from optional header
    mov eax, [rsi + OPT_ENTRY_POINT]
    mov [rel KernelEntry], rax      ; Save RVA of entry point
    mov rax, [rsi + OPT_IMAGE_BASE]
    mov rbx, rax                    ; RBX = original ImageBase
    mov eax, [rsi + OPT_SIZE_IMAGE]
    mov r12d, eax                   ; R12 = SizeOfImage
    mov [rel KernelImageSize], r12  ; Save for BootInfo (r12 gets clobbered by relocation)
    mov eax, [rsi + OPT_SIZE_HEADERS]
    mov r13d, eax                   ; R13 = SizeOfHeaders

    ; Get relocation directory
    mov eax, [rsi + OPT_DATA_DIR + DATA_DIR_BASERELOC * 8]      ; RVA
    mov r14d, eax                   ; R14 = reloc RVA
    mov eax, [rsi + OPT_DATA_DIR + DATA_DIR_BASERELOC * 8 + 4]  ; Size
    mov r15d, eax                   ; R15 = reloc size

    ; Debug: Print PE info
    mov al, 'P'
    call SerialPutChar
    mov al, 'E'
    call SerialPutChar
    mov al, ':'
    call SerialPutChar
    mov rax, rbx                    ; ImageBase
    call SerialPutHex64
    mov al, ' '
    call SerialPutChar
    mov al, 'E'
    call SerialPutChar
    mov eax, [rel KernelEntry]
    call SerialPutHex64
    mov al, ' '
    call SerialPutChar
    mov al, 'S'
    call SerialPutChar
    mov rax, r12                    ; SizeOfImage
    call SerialPutHex64
    mov al, 10
    call SerialPutChar

    ; Copy kernel to pre-allocated region at 128 MB
    ; (Memory was already allocated at EfiMain entry)
    lea rcx, [rel MsgRelocating]
    call PrintString

    ; Debug: print SizeOfImage (r12) and verify kernel fits
    mov al, 'Z'
    call SerialPutChar
    mov rax, r12
    call SerialPutHex64
    mov al, '/'
    call SerialPutChar
    ; Calculate pages needed
    mov rax, r12
    add rax, 0xFFF
    shr rax, 12
    call SerialPutHex64
    mov al, 10
    call SerialPutChar

    ; Verify kernel fits in allocated region
    cmp r12, MAX_KERNEL_SIZE
    ja .error_alloc                 ; Kernel too large!
    ; Restore section info
    pop rdx                         ; Optional header size (discarded now)
    pop rcx                         ; Number of sections

    ; RSI should still point to optional header
    ; Calculate section headers: OptHeader + OptHeaderSize
    mov rdi, [rel KernelBuffer]
    mov eax, [rdi + DOS_LFANEW]
    add rdi, rax
    add rdi, 4 + COFF_HEADER_SIZE   ; Skip PE sig + COFF header
    add rdi, rdx                    ; Skip optional header
    ; RDI now points to first section header

    ; Copy headers first
    mov rsi, [rel KernelBuffer]
    mov rdi, [rel KernelBase]
    mov rcx, r13                    ; SizeOfHeaders
    rep movsb

    ; Get section headers again
    mov rsi, [rel KernelBuffer]
    mov eax, [rsi + DOS_LFANEW]
    add rsi, rax
    add rsi, 4 + COFF_HEADER_SIZE
    movzx rdx, word [rsi - COFF_HEADER_SIZE + COFF_SIZE_OPT_HDR]
    add rsi, rdx                    ; RSI = section headers

    ; Get number of sections again
    mov rdi, [rel KernelBuffer]
    mov eax, [rdi + DOS_LFANEW]
    add rdi, rax
    movzx ecx, word [rdi + 4 + COFF_NUM_SECTIONS]
    ; ECX = number of sections

    ; Copy each section
.copy_sections:
    push rcx

    ; Get section info
    mov eax, [rsi + SEC_VIRT_ADDR]
    mov r8d, eax                    ; R8 = VirtualAddress (RVA)
    mov eax, [rsi + SEC_RAW_SIZE]
    mov r9d, eax                    ; R9 = SizeOfRawData
    mov eax, [rsi + SEC_RAW_PTR]
    mov r10d, eax                   ; R10 = PointerToRawData

    ; Calculate destination
    mov rdi, [rel KernelBase]
    add rdi, r8                     ; Dest = KernelBase + RVA

    ; Calculate source
    mov rsi, [rel KernelBuffer]
    add rsi, r10                    ; Src = Buffer + FileOffset

    ; Copy section
    mov rcx, r9
    test rcx, rcx
    jz .next_section
    rep movsb

.next_section:
    ; Move to next section header
    mov rsi, [rel KernelBuffer]
    pop rcx
    dec rcx
    jz .sections_done

    push rcx
    ; Recalculate section header pointer
    mov rdi, [rel KernelBuffer]
    mov eax, [rdi + DOS_LFANEW]
    add rdi, rax
    movzx edx, word [rdi + 4 + COFF_SIZE_OPT_HDR]
    add rdi, 4 + COFF_HEADER_SIZE
    add rdi, rdx
    ; RDI = first section header
    ; We need to skip (original_count - remaining_count) sections
    ; This is getting complicated. Let me use a different approach.
    pop rcx
    ; Just recalculate based on remaining count
    jmp .copy_sections_alt

.copy_sections_alt:
    ; Simplified: iterate through all sections
    mov rsi, [rel KernelBuffer]
    mov eax, [rsi + DOS_LFANEW]
    add rsi, rax
    movzx r11d, word [rsi + 4 + COFF_NUM_SECTIONS]  ; Total sections
    movzx edx, word [rsi + 4 + COFF_SIZE_OPT_HDR]
    add rsi, 4 + COFF_HEADER_SIZE
    add rsi, rdx                                    ; Section headers

    xor ecx, ecx                    ; Section index
.copy_loop:
    cmp ecx, r11d
    jge .sections_done

    push rcx
    push rsi

    ; Get section info (rsi points to current section header)
    imul eax, ecx, SEC_HEADER_SIZE
    add rsi, rax

    mov eax, [rsi + SEC_VIRT_ADDR]
    mov r8d, eax                    ; R8 = VirtualAddress (RVA)
    mov eax, [rsi + SEC_RAW_SIZE]
    mov r9d, eax                    ; R9 = SizeOfRawData
    mov eax, [rsi + SEC_RAW_PTR]
    mov r10d, eax                   ; R10 = PointerToRawData

    ; Skip if no raw data
    test r9d, r9d
    jz .copy_next

    ; Calculate destination
    mov rdi, [rel KernelBase]
    add rdi, r8

    ; Calculate source
    mov rsi, [rel KernelBuffer]
    add rsi, r10

    ; Copy section
    mov rcx, r9
    rep movsb

.copy_next:
    pop rsi
    pop rcx
    inc ecx
    jmp .copy_loop

.sections_done:
    ; Apply relocations
    ; RBX = original ImageBase
    ; R14 = reloc RVA
    ; R15 = reloc size

    test r15d, r15d
    jz .reloc_done                  ; No relocations

    ; Calculate delta
    mov rax, [rel KernelBase]
    sub rax, rbx                    ; Delta = NewBase - OldBase
    mov r12, rax                    ; R12 = delta

    ; Find relocation data in copied image
    mov rsi, [rel KernelBase]
    add rsi, r14                    ; RSI = relocation table

    mov rcx, r15                    ; Bytes remaining

.reloc_block:
    cmp rcx, 8
    jl .reloc_done

    ; Read block header
    mov eax, [rsi + RELOC_PAGE_RVA]
    mov r8d, eax                    ; R8 = PageRVA
    mov eax, [rsi + RELOC_BLOCK_SIZE]
    mov r9d, eax                    ; R9 = BlockSize

    test r9d, r9d
    jz .reloc_done

    ; Calculate entries in this block
    mov r10d, r9d
    sub r10d, 8                     ; Subtract header
    shr r10d, 1                     ; Divide by 2 (each entry is 2 bytes)

    ; Process entries
    lea rdi, [rsi + RELOC_ENTRIES]

.reloc_entry:
    test r10d, r10d
    jz .reloc_next_block

    movzx eax, word [rdi]
    mov r11d, eax
    shr r11d, 12                    ; Type (high 4 bits)
    and eax, 0xFFF                  ; Offset (low 12 bits)

    cmp r11d, RELOC_ABSOLUTE
    je .reloc_skip                  ; Skip absolute (no-op)

    cmp r11d, RELOC_DIR64
    jne .reloc_skip                 ; Skip unknown types

    ; Apply 64-bit relocation
    mov rbx, [rel KernelBase]
    add rbx, r8                     ; Page base
    add rbx, rax                    ; + offset
    add qword [rbx], r12            ; Apply delta

.reloc_skip:
    add rdi, 2
    dec r10d
    jmp .reloc_entry

.reloc_next_block:
    sub rcx, r9                     ; Subtract block size
    add rsi, r9                     ; Move to next block
    jmp .reloc_block

.reloc_done:
    ; Debug: relocations done
    mov al, 'R'
    call SerialPutChar

    ; Calculate final entry point
    mov rax, [rel KernelBase]
    add rax, [rel KernelEntry]      ; Base + EntryPointRVA
    mov [rel KernelEntry], rax

    ; Debug: Print kernel base and entry point
    mov al, '@'
    call SerialPutChar
    mov rax, [rel KernelBase]
    call SerialPutHex64
    mov al, ':'
    call SerialPutChar
    mov rax, [rel KernelEntry]
    call SerialPutHex64
    mov al, 10
    call SerialPutChar

    ; === Complete boot preparation ===
    ; 1. Copy all files from UEFI FS to our file region
    ; 2. Find ACPI RSDP
    ; 3. Get memory map
    ; 4. Build BootInfo structure
    ; 5. Exit boot services
    ; 6. Jump to kernel with BootInfo pointer

    ; Step 1: Copy all files to the 32MB region
    call CopyAllFiles

    ; Step 2: Find ACPI RSDP from UEFI configuration tables
    call FindAcpiRsdp

    ; Step 3: Get UEFI memory map (needed for ExitBootServices)
    call GetUefiMemoryMap
    test rax, rax
    jnz .error_memmap

    ; Step 4: Build the BootInfo structure at 1MB
    ; First, copy critical data that we need after ExitBootServices
    ; to the BootInfo region which is in safe memory

    ; Clear BootInfo structure
    mov rdi, BOOTINFO_ADDR
    mov rcx, BOOTINFO_SIZE / 8
    xor eax, eax
    rep stosq

    ; Fill in BootInfo header (reset RDI after rep stosq moved it)
    mov rdi, BOOTINFO_ADDR
    mov rax, BOOTINFO_MAGIC
    mov [rdi + BI_MAGIC], rax
    mov dword [rdi + BI_VERSION], BOOTINFO_VERSION

    ; Flags
    mov eax, BIF_HAS_SERIAL         ; Always have serial
    cmp qword [rel AcpiRsdp], 0
    je .no_acpi_flag
    or eax, BIF_HAS_ACPI
.no_acpi_flag:
    mov [rdi + BI_FLAGS], eax

    ; Memory map
    lea rax, [rel UefiMemMapBuffer]
    mov [rdi + BI_MEMMAP_ADDR], rax
    mov rax, [rel UefiMemMapSize]
    xor edx, edx
    mov rcx, [rel UefiDescSize]
    div rcx
    mov [rdi + BI_MEMMAP_ENTRIES], eax
    mov rax, [rel UefiDescSize]
    mov [rdi + BI_MEMMAP_ENTRYSIZE], eax

    ; Kernel location
    mov rax, KERNEL_ADDR
    mov [rdi + BI_KERNEL_PHYS], rax
    mov rax, 0x140000000            ; Standard PE base
    mov [rdi + BI_KERNEL_VIRT], rax
    mov rax, [rel KernelImageSize]  ; SizeOfImage (saved before relocation clobbered r12)
    mov [rdi + BI_KERNEL_SIZE], rax
    mov rax, [rel KernelEntry]
    sub rax, KERNEL_ADDR            ; Convert to offset
    mov [rdi + BI_KERNEL_ENTRY], rax

    ; Loaded files
    mov rax, BOOTINFO_ADDR
    add rax, BOOTINFO_SIZE          ; File table follows BootInfo header
    mov [rdi + BI_FILES_ADDR], rax
    mov eax, [rel FilesCount]
    mov [rdi + BI_FILES_COUNT], eax

    ; ACPI
    mov rax, [rel AcpiRsdp]
    mov [rdi + BI_ACPI_RSDP], rax

    ; Serial port
    mov qword [rdi + BI_SERIAL_PORT], 0x3F8

    ; Store kernel entry point in BootInfo region for safe access after ExitBootServices
    ; We'll read it from there instead of from loader memory
    mov rax, [rel KernelEntry]
    mov [rdi + 248], rax            ; Use last reserved slot temporarily

    ; Step 5: Exit boot services
    lea rcx, [rel MsgExiting]
    call PrintString

    mov rcx, [rel ImageHandle]
    mov rdx, [rel UefiMemMapKey]
    mov rax, [rel BootServices]
    sub rsp, 32
    call [rax + 232]                ; BS_EXIT_BOOT_SERVICES
    add rsp, 32
    test rax, rax
    jnz .exit_retry

.boot_services_exited:
    ; === Boot services are now gone - no more UEFI calls! ===

    ; Print status via serial only
    mov al, 'K'
    call SerialPutChar
    mov al, '!'
    call SerialPutChar
    mov al, 10
    call SerialPutChar

    ; Step 6: Jump to kernel with BootInfo pointer
    ; Read kernel entry from BootInfo region (safe memory)
    mov rdi, BOOTINFO_ADDR
    mov rax, [rdi + 248]            ; Kernel entry stored here
    mov rcx, BOOTINFO_ADDR          ; BootInfo* in RCX
    jmp rax

    ; Should never return
    xor eax, eax
    ret

.exit_retry:
    ; ExitBootServices failed - memory map may have changed
    mov al, 'r'
    call SerialPutChar

    ; Get fresh memory map
    call GetUefiMemoryMap
    test rax, rax
    jnz .error_exit

    ; Update BootInfo with new map
    mov rdi, BOOTINFO_ADDR
    lea rax, [rel UefiMemMapBuffer]
    mov [rdi + BI_MEMMAP_ADDR], rax
    mov rax, [rel UefiMemMapSize]
    xor edx, edx
    mov rcx, [rel UefiDescSize]
    div rcx
    mov [rdi + BI_MEMMAP_ENTRIES], eax
    mov rax, [rel UefiDescSize]
    mov [rdi + BI_MEMMAP_ENTRYSIZE], eax

    ; Retry ExitBootServices
    mov rcx, [rel ImageHandle]
    mov rdx, [rel UefiMemMapKey]
    mov rax, [rel BootServices]
    sub rsp, 32
    call [rax + 232]
    add rsp, 32
    test rax, rax
    jz .boot_services_exited

.error_exit:
    mov al, 'X'
    call SerialPutChar
    lea rcx, [rel MsgError]
    call PrintString
    lea rcx, [rel MsgExitFailed]
    call PrintString
    mov eax, 1
    ret

.error_memmap:
    mov al, 'M'
    call SerialPutChar
    lea rcx, [rel MsgError]
    call PrintString
    lea rcx, [rel MsgAllocFailed]
    call PrintString
    mov eax, 1
    ret

.error_bootinfo_alloc:
    mov al, 'B'
    call SerialPutChar
    mov al, 'I'
    call SerialPutChar
    jmp .error_alloc_common

.error_files_alloc:
    mov al, 'F'
    call SerialPutChar
    mov al, 'A'
    call SerialPutChar
    jmp .error_alloc_common

.error_kernel_alloc:
    mov al, 'K'
    call SerialPutChar
    mov al, 'A'
    call SerialPutChar
    jmp .error_alloc_common

.error_alloc:
    mov al, 'A'
    call SerialPutChar
    ; Fall through

.error_alloc_common:
    lea rcx, [rel MsgError]
    call PrintString
    lea rcx, [rel MsgAllocFailed]
    call PrintString
    mov eax, 1
    ret

.error_li:
    mov al, 'L'
    call SerialPutChar
    mov al, 'I'
    call SerialPutChar
    jmp .error_common

.error_fs:
    mov al, 'F'
    call SerialPutChar
    mov al, 'S'
    call SerialPutChar
    jmp .error_common

.error_file:
    mov al, 'F'
    call SerialPutChar
    jmp .error_common

.error_common:
    lea rcx, [rel MsgError]
    call PrintString
    lea rcx, [rel MsgFileFailed]
    call PrintString
    mov eax, 1
    ret

.error_pe:
    mov al, 'P'
    call SerialPutChar
    lea rcx, [rel MsgError]
    call PrintString
    lea rcx, [rel MsgBadPE]
    call PrintString
    mov eax, 1
    ret

;; ============================================================================
;; Helper: Print UCS-2 string via ConOut
;; Input: RCX = pointer to null-terminated UCS-2 string
;; ============================================================================
PrintString:
    push rbx
    mov rbx, rcx                    ; String pointer
    mov rcx, [rel ConOut]
    test rcx, rcx
    jz .print_done
    ; UEFI protocol: first arg = self, second arg = string
    ; ConOut->OutputString(ConOut, String)
    mov rdx, rbx                    ; String
    ; rcx already has ConOut (self)
    sub rsp, 32
    call [rcx + 8]                  ; OutputString is at offset 8 in protocol
    add rsp, 32
.print_done:
    pop rbx
    ret

;; ============================================================================
;; Helper: Get UEFI memory map
;; Returns: RAX = 0 on success, non-zero on failure
;; ============================================================================
GetUefiMemoryMap:
    push rbx
    push rsi
    push rdi

    ; Reset buffer size
    mov qword [rel UefiMemMapSize], MEMMAP_BUFFER_SIZE

    ; GetMemoryMap(&MapSize, Buffer, &MapKey, &DescSize, &DescVersion)
    lea rcx, [rel UefiMemMapSize]
    lea rdx, [rel UefiMemMapBuffer]
    lea r8, [rel UefiMemMapKey]
    lea r9, [rel UefiDescSize]
    lea rax, [rel UefiDescVersion]
    push rax                        ; 5th param on stack
    sub rsp, 32                     ; Shadow space
    mov rax, [rel BootServices]
    call [rax + 56]                 ; BS_GET_MEMORY_MAP = 56
    add rsp, 40                     ; Clean up stack

    pop rdi
    pop rsi
    pop rbx
    ret

;; ============================================================================
;; Helper: Find ACPI RSDP from UEFI configuration tables
;; Stores result in AcpiRsdp (0 if not found)
;; ============================================================================
FindAcpiRsdp:
    push rbx
    push rsi
    push rdi
    push r12
    push r13

    ; SystemTable->NumberOfTableEntries at offset 104
    ; SystemTable->ConfigurationTable at offset 112
    mov rax, [rel SystemTable]
    mov ecx, [rax + 104]            ; NumberOfTableEntries
    mov rsi, [rax + 112]            ; ConfigurationTable array

    test ecx, ecx
    jz .acpi_not_found

.acpi_search_loop:
    ; Each entry is 24 bytes: GUID (16) + VendorTable (8)
    ; Compare against ACPI 2.0 GUID first (preferred)
    lea rdi, [rel Acpi20Guid]
    mov r12, rsi                    ; Save table entry pointer

    ; Compare 16 bytes of GUID
    mov rax, [rsi]
    cmp rax, [rdi]
    jne .try_acpi10
    mov rax, [rsi + 8]
    cmp rax, [rdi + 8]
    jne .try_acpi10

    ; Found ACPI 2.0!
    mov rax, [r12 + 16]             ; VendorTable = RSDP pointer
    mov [rel AcpiRsdp], rax
    jmp .acpi_found

.try_acpi10:
    ; Compare against ACPI 1.0 GUID
    lea rdi, [rel Acpi10Guid]
    mov rax, [rsi]
    cmp rax, [rdi]
    jne .acpi_next
    mov rax, [rsi + 8]
    cmp rax, [rdi + 8]
    jne .acpi_next

    ; Found ACPI 1.0! (only use if we haven't found 2.0)
    cmp qword [rel AcpiRsdp], 0
    jne .acpi_next                  ; Already have 2.0, skip 1.0
    mov rax, [r12 + 16]
    mov [rel AcpiRsdp], rax

.acpi_next:
    add rsi, 24                     ; Next entry
    dec ecx
    jnz .acpi_search_loop

.acpi_found:
.acpi_not_found:
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

;; ============================================================================
;; Helper: Build BootInfo structure
;; Must be called after GetUefiMemoryMap and FindAcpiRsdp
;; ============================================================================
BuildBootInfo:
    push rbx
    push rsi
    push rdi

    lea rdi, [rel BootInfo]

    ; Clear structure first
    push rdi
    mov rcx, BOOTINFO_SIZE / 8
    xor eax, eax
    rep stosq
    pop rdi

    ; Fill in header
    mov rax, BOOTINFO_MAGIC
    mov [rdi + BI_MAGIC], rax
    mov dword [rdi + BI_VERSION], BOOTINFO_VERSION

    ; Flags
    mov eax, BIF_HAS_SERIAL         ; Always have serial
    cmp qword [rel AcpiRsdp], 0
    je .no_acpi_flag
    or eax, BIF_HAS_ACPI
.no_acpi_flag:
    mov [rdi + BI_FLAGS], eax

    ; Memory map (point to UEFI map for now - kernel will convert)
    ; TODO: Convert UEFI map to our format here instead
    lea rax, [rel UefiMemMapBuffer]
    mov [rdi + BI_MEMMAP_ADDR], rax

    ; Calculate entry count from size / descriptor size
    mov rax, [rel UefiMemMapSize]
    xor edx, edx
    mov rcx, [rel UefiDescSize]
    div rcx                         ; RAX = entry count
    mov [rdi + BI_MEMMAP_ENTRIES], eax
    mov rax, [rel UefiDescSize]
    mov [rdi + BI_MEMMAP_ENTRYSIZE], eax

    ; Kernel location
    mov rax, [rel KernelBase]
    mov [rdi + BI_KERNEL_PHYS], rax

    ; Get original virtual base from saved value (r11 was ImageBase)
    ; For now, use 0x140000000 (standard PE base)
    mov rax, 0x140000000
    mov [rdi + BI_KERNEL_VIRT], rax

    mov rax, [rel KernelImageSize]  ; SizeOfImage (saved before relocation)
    mov [rdi + BI_KERNEL_SIZE], rax

    mov rax, [rel KernelEntry]
    sub rax, [rel KernelBase]       ; Convert back to offset
    mov [rdi + BI_KERNEL_ENTRY], rax

    ; Note: BuildBootInfo is dead code - main path builds BootInfo inline
    mov qword [rdi + BI_FILES_ADDR], 0
    mov dword [rdi + BI_FILES_COUNT], 0

    ; ACPI RSDP
    mov rax, [rel AcpiRsdp]
    mov [rdi + BI_ACPI_RSDP], rax

    ; No framebuffer for now
    mov qword [rdi + BI_FB_ADDR], 0

    ; Serial port
    mov qword [rdi + BI_SERIAL_PORT], 0x3F8

    pop rdi
    pop rsi
    pop rbx
    ret

;; ============================================================================
;; Helper: Copy all files from UEFI filesystem to our file region
;; Must be called before ExitBootServices
;; Populates file table at BootInfoBase + BOOTINFO_SIZE
;; ============================================================================
CopyAllFiles:
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15

    ; Print status
    lea rcx, [rel MsgCopyingFiles]
    call PrintString

    ; Initialize file copying state
    mov rax, FILES_ADDR
    mov [rel FilesNextAddr], rax
    mov qword [rel FilesCount], 0

    ; RootDir is already open from kernel loading
    ; Copy files from root directory
    mov rcx, [rel RootDir]
    xor edx, edx                    ; Path prefix length = 0
    call CopyFilesFromDir

    ; Print file count
    mov al, 'F'
    call SerialPutChar
    mov al, '='
    call SerialPutChar
    mov rax, [rel FilesCount]
    call SerialPutHex64
    mov al, 10
    call SerialPutChar

    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

;; ============================================================================
;; Helper: Copy files from a directory (recursive)
;; Input: RCX = directory handle, RDX = path prefix length (unused for now)
;; ============================================================================
CopyFilesFromDir:
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15

    mov r12, rcx                    ; R12 = directory handle
    mov r13, rdx                    ; R13 = path prefix length

    ; Rewind directory to beginning
    mov rcx, r12
    xor rdx, rdx                    ; Position = 0
    sub rsp, 32
    call [r12 + EFI_FILE_SET_POS]   ; SetPosition(Dir, 0)
    add rsp, 32

.read_entry:
    ; Read next directory entry
    mov qword [rel EnumBufferSize], 512
    mov rcx, r12                    ; Directory handle
    lea rdx, [rel EnumBufferSize]   ; Pointer to buffer size
    lea r8, [rel EnumBuffer]        ; Buffer
    sub rsp, 32
    call [r12 + EFI_FILE_READ]      ; Read(Dir, &Size, Buffer)
    add rsp, 32

    ; Check if we got anything
    mov rax, [rel EnumBufferSize]
    test rax, rax
    jz .done                        ; No more entries

    ; Get entry info from buffer
    lea rsi, [rel EnumBuffer]
    mov rax, [rsi + FI_ATTRIBUTE]
    mov r14, [rsi + FI_FILE_SIZE]   ; R14 = file size

    ; Check if it's a directory
    test rax, EFI_FILE_DIRECTORY
    jnz .check_subdir

    ; It's a file - copy it
    ; Skip if it's the kernel (already loaded separately)
    lea rdi, [rsi + FI_FILENAME]
    call IsKernelFile
    test al, al
    jnz .read_entry                 ; Skip kernel file

    ; Copy this file
    mov rcx, r12                    ; Directory handle
    lea rdx, [rsi + FI_FILENAME]    ; Filename (UCS-2)
    mov r8, r14                     ; File size
    call CopyOneFile
    jmp .read_entry

.check_subdir:
    ; It's a directory - check if we should recurse
    lea rdi, [rsi + FI_FILENAME]

    ; Skip "." and ".."
    cmp word [rdi], '.'
    jne .not_dot
    cmp word [rdi + 2], 0
    je .read_entry                  ; Skip "."
    cmp word [rdi + 2], '.'
    jne .not_dot
    cmp word [rdi + 4], 0
    je .read_entry                  ; Skip ".."

.not_dot:
    ; Skip EFI directory (contains bootloader, not needed)
    cmp word [rdi], 'E'
    jne .recurse
    cmp word [rdi + 2], 'F'
    jne .recurse
    cmp word [rdi + 4], 'I'
    jne .recurse
    cmp word [rdi + 6], 0
    je .read_entry                  ; Skip "EFI" directory

.recurse:
    ; Open subdirectory and recurse
    mov rcx, r12                    ; Parent directory handle
    lea rdx, [rel TempFileHandle]   ; Output handle
    lea r8, [rsi + FI_FILENAME]     ; Subdirectory name
    mov r9, EFI_FILE_MODE_READ
    xor rax, rax
    push rax                        ; Attributes = 0
    sub rsp, 32
    call [r12 + EFI_FILE_OPEN]      ; Open(Dir, &SubDir, Name, Mode, 0)
    add rsp, 40
    test rax, rax
    jnz .read_entry                 ; Failed to open, skip

    ; Save subdirectory handle (CopyFilesFromDir/CopyOneFile will clobber TempFileHandle)
    mov rax, [rel TempFileHandle]
    push rax

    ; Recurse into subdirectory
    mov rcx, rax                    ; Use saved handle
    xor edx, edx
    call CopyFilesFromDir

    ; Restore and close subdirectory
    pop rcx                         ; Subdirectory handle
    sub rsp, 32
    call [rcx + EFI_FILE_CLOSE]
    add rsp, 32

    jmp .read_entry

.done:
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret

;; ============================================================================
;; Helper: Check if filename is kernel (to skip it)
;; Input: RDI = UCS-2 filename
;; Output: AL = 1 if kernel, 0 otherwise
;; ============================================================================
IsKernelFile:
    ; Check for "KERNEL.BIN" (case insensitive)
    cmp word [rdi], 'K'
    je .check_rest
    cmp word [rdi], 'k'
    jne .not_kernel
.check_rest:
    cmp word [rdi + 2], 'E'
    je .k2
    cmp word [rdi + 2], 'e'
    jne .not_kernel
.k2:
    cmp word [rdi + 4], 'R'
    je .k3
    cmp word [rdi + 4], 'r'
    jne .not_kernel
.k3:
    cmp word [rdi + 6], 'N'
    je .k4
    cmp word [rdi + 6], 'n'
    jne .not_kernel
.k4:
    cmp word [rdi + 8], 'E'
    je .k5
    cmp word [rdi + 8], 'e'
    jne .not_kernel
.k5:
    cmp word [rdi + 10], 'L'
    je .k6
    cmp word [rdi + 10], 'l'
    jne .not_kernel
.k6:
    cmp word [rdi + 12], '.'
    jne .not_kernel
    cmp word [rdi + 14], 'B'
    je .k7
    cmp word [rdi + 14], 'b'
    jne .not_kernel
.k7:
    cmp word [rdi + 16], 'I'
    je .k8
    cmp word [rdi + 16], 'i'
    jne .not_kernel
.k8:
    cmp word [rdi + 18], 'N'
    je .k9
    cmp word [rdi + 18], 'n'
    jne .not_kernel
.k9:
    cmp word [rdi + 20], 0
    jne .not_kernel

    mov al, 1
    ret

.not_kernel:
    xor al, al
    ret

;; ============================================================================
;; Helper: Copy one file to file region
;; Input: RCX = directory handle, RDX = filename (UCS-2), R8 = file size
;; ============================================================================
CopyOneFile:
    push rbx
    push rsi
    push rdi
    push r12
    push r13
    push r14
    push r15

    mov r12, rcx                    ; R12 = directory handle
    mov r13, rdx                    ; R13 = filename pointer
    mov r14, r8                     ; R14 = file size

    ; Print dot for progress
    mov al, '.'
    call SerialPutChar

    ; Check if we have space
    mov rax, [rel FilesNextAddr]
    add rax, r14
    cmp rax, FILES_ADDR + FILES_RESERVED
    ja .no_space                    ; Out of space

    ; Check if we have file table space
    mov rax, [rel FilesCount]
    cmp rax, MAX_LOADED_FILES
    jae .no_space

    ; Open the file
    mov rcx, r12                    ; Directory handle
    lea rdx, [rel TempFileHandle]   ; Output handle
    mov r8, r13                     ; Filename
    mov r9, EFI_FILE_MODE_READ
    xor rax, rax
    push rax                        ; Attributes = 0
    sub rsp, 32
    call [r12 + EFI_FILE_OPEN]      ; Open(Dir, &File, Name, Mode, 0)
    add rsp, 40
    test rax, rax
    jnz .open_failed

    ; Read file contents to our region
    mov r15, [rel FilesNextAddr]    ; R15 = destination address

    ; Read in chunks (file protocol may not read everything at once)
    mov rbx, r14                    ; RBX = bytes remaining
.read_loop:
    test rbx, rbx
    jz .read_done

    mov rcx, [rel TempFileHandle]
    lea rdx, [rel KernelFileSize]   ; Reuse this as temp size var
    mov [rdx], rbx                  ; Request all remaining bytes
    mov r8, r15                     ; Destination
    add r8, r14
    sub r8, rbx                     ; Current position in destination
    sub rsp, 32
    call [rcx + EFI_FILE_READ]      ; Read(File, &Size, Buffer)
    add rsp, 32
    test rax, rax
    jnz .read_failed

    mov rax, [rel KernelFileSize]   ; Bytes actually read
    test rax, rax
    jz .read_done                   ; EOF
    sub rbx, rax
    jmp .read_loop

.read_done:
    ; Close the file
    mov rcx, [rel TempFileHandle]
    sub rsp, 32
    call [rcx + EFI_FILE_CLOSE]
    add rsp, 32

    ; Create file table entry
    ; Entry address = BootInfoBase + BOOTINFO_SIZE + (FilesCount * LOADED_FILE_SIZE)
    mov rax, [rel FilesCount]
    imul rax, LOADED_FILE_SIZE
    add rax, BOOTINFO_ADDR
    add rax, BOOTINFO_SIZE
    mov rdi, rax                    ; RDI = entry address

    ; Fill in entry
    mov rax, r15                    ; Physical address
    mov [rdi + LF_PHYS_ADDR], rax
    mov rax, r14                    ; Size
    mov [rdi + LF_SIZE], rax

    ; Copy filename (convert UCS-2 to ASCII, max 63 chars + null)
    lea rsi, [rdi + LF_NAME]
    mov rcx, r13                    ; Source filename (UCS-2)
    mov r8d, 63                     ; Max chars
.copy_name:
    test r8d, r8d
    jz .name_done
    movzx eax, word [rcx]
    test ax, ax
    jz .name_done
    mov [rsi], al                   ; Store low byte (ASCII)
    inc rsi
    add rcx, 2
    dec r8d
    jmp .copy_name
.name_done:
    mov byte [rsi], 0               ; Null terminate

    ; Set flags (could detect DLL vs data here)
    mov dword [rdi + LF_FLAGS], 0
    mov dword [rdi + LF_RESERVED], 0

    ; Update state
    mov rax, r15
    add rax, r14
    ; Align to 4KB for next file
    add rax, 0xFFF
    and rax, ~0xFFF
    mov [rel FilesNextAddr], rax

    inc qword [rel FilesCount]

.open_failed:
.read_failed:
.no_space:
    pop r15
    pop r14
    pop r13
    pop r12
    pop rdi
    pop rsi
    pop rbx
    ret
