; ProtonOS UEFI Bootloader
; Loads KERNEL.BIN at a predictable address (32MB, 64MB, or 128MB)
; This allows GDB to reliably find and debug the kernel.
;
; The kernel is a PE/COFF binary but NOT an EFI application - this loader
; handles relocations and jumps to the kernel entry point directly.
; Currently passes UEFI ImageHandle/SystemTable, but this will change
; to a platform-agnostic BootInfo structure in the future.
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
;; Preferred kernel load addresses (in order of preference)
;; Must be above 21MB (0x01500000) where conventional memory starts
;; ============================================================================

KERNEL_ADDR_1       equ 0x02000000  ; 32 MB
KERNEL_ADDR_2       equ 0x04000000  ; 64 MB
KERNEL_ADDR_3       equ 0x08000000  ; 128 MB

; Maximum kernel size we support (8 MB for growth room)
MAX_KERNEL_SIZE     equ 0x00800000

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

; Kernel load info
KernelBuffer:       dq 0            ; Address where kernel file was loaded
KernelFileSize:     dq 0            ; Size of kernel file
KernelBase:         dq 0            ; Final kernel base address
KernelEntry:        dq 0            ; Kernel entry point address

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

    ; Try to allocate at preferred addresses
    lea rcx, [rel MsgRelocating]
    call PrintString

    ; Debug: print SizeOfImage (r12) and pages needed
    mov al, 'Z'
    call SerialPutChar
    mov rax, r12
    call SerialPutHex64
    mov al, '/'
    call SerialPutChar
    ; Calculate pages
    mov rax, r12
    add rax, 0xFFF
    shr rax, 12
    call SerialPutHex64
    mov al, 10
    call SerialPutChar

    ; Try KERNEL_ADDR_1 (32 MB)
    ; AllocatePages(Type, MemoryType, Pages, *Memory)
    ; For ALLOCATE_ADDRESS, *Memory = pointer to desired address
    mov al, 'a'
    call SerialPutChar
    mov al, '1'
    call SerialPutChar

    mov qword [rel KernelBase], KERNEL_ADDR_1
    mov rcx, ALLOCATE_ADDRESS
    mov rdx, EFI_LOADER_DATA
    mov r8, r12
    add r8, 0xFFF
    shr r8, 12                      ; Pages needed
    lea r9, [rel KernelBase]        ; Pointer to address variable
    sub rsp, 32
    mov rax, [rel BootServices]
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32

    ; Debug: print return status
    push rax
    mov al, '='
    call SerialPutChar
    pop rax
    push rax
    call SerialPutHex64
    pop rax

    test rax, rax
    jz .alloc_success

    ; Try KERNEL_ADDR_2 (64 MB)
    mov al, 'a'
    call SerialPutChar
    mov al, '2'
    call SerialPutChar

    mov qword [rel KernelBase], KERNEL_ADDR_2
    mov rcx, ALLOCATE_ADDRESS
    mov rdx, EFI_LOADER_DATA
    mov r8, r12
    add r8, 0xFFF
    shr r8, 12
    lea r9, [rel KernelBase]
    sub rsp, 32
    mov rax, [rel BootServices]
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32

    ; Debug: print return status
    push rax
    mov al, '='
    call SerialPutChar
    pop rax
    push rax
    call SerialPutHex64
    pop rax

    test rax, rax
    jz .alloc_success

    ; Try KERNEL_ADDR_3 (128 MB)
    mov al, 'a'
    call SerialPutChar
    mov al, '3'
    call SerialPutChar

    mov qword [rel KernelBase], KERNEL_ADDR_3
    mov rcx, ALLOCATE_ADDRESS
    mov rdx, EFI_LOADER_DATA
    mov r8, r12
    add r8, 0xFFF
    shr r8, 12
    lea r9, [rel KernelBase]
    sub rsp, 32
    mov rax, [rel BootServices]
    call [rax + BS_ALLOCATE_PAGES]
    add rsp, 32

    ; Debug: print return status
    push rax
    mov al, '='
    call SerialPutChar
    pop rax
    push rax
    call SerialPutHex64
    pop rax

    test rax, rax
    jnz .error_alloc

.alloc_success:
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

    ; Print starting message
    lea rcx, [rel MsgStarting]
    call PrintString

    ; Debug: About to jump
    mov al, 'J'
    call SerialPutChar

    ; Jump to kernel
    ; Windows x64 ABI: ImageHandle in rcx, SystemTable in rdx
    mov rcx, [rel ImageHandle]
    mov rdx, [rel SystemTable]
    mov rax, [rel KernelEntry]
    jmp rax

    ; Should never return
    xor eax, eax
    ret

.error_alloc:
    mov al, 'A'
    call SerialPutChar
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
