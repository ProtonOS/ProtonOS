; netos nernel - x64 native layer
; Minimal CPU intrinsics that cannot be expressed in C#

BITS 64
DEFAULT REL

section .text

;; ==================== Constants ====================

COM1_PORT       equ 0x3F8
COM1_DATA       equ COM1_PORT + 0   ; Data register
COM1_IER        equ COM1_PORT + 1   ; Interrupt Enable Register
COM1_FCR        equ COM1_PORT + 2   ; FIFO Control Register
COM1_LCR        equ COM1_PORT + 3   ; Line Control Register
COM1_MCR        equ COM1_PORT + 4   ; Modem Control Register
COM1_LSR        equ COM1_PORT + 5   ; Line Status Register
COM1_DLL        equ COM1_PORT + 0   ; Divisor Latch Low (when DLAB=1)
COM1_DLH        equ COM1_PORT + 1   ; Divisor Latch High (when DLAB=1)

;; ==================== UEFI Entry Point ====================

global EfiMain

; EFI_STATUS EfiMain(EFI_HANDLE ImageHandle, EFI_SYSTEM_TABLE *SystemTable)
EfiMain:
    ; Save callee-saved registers
    push rbx
    push rsi
    push rdi

    ; Initialize serial port
    call serial_init

    ; Print boot message
    lea rcx, [rel boot_msg]
    call serial_print

    ; Halt loop - we made it!
.halt_loop:
    hlt
    jmp .halt_loop

;; ==================== Serial Port ====================

; Initialize COM1 serial port at 115200 baud
serial_init:
    ; Disable interrupts
    mov dx, COM1_IER
    xor al, al
    out dx, al

    ; Enable DLAB (set baud rate divisor)
    mov dx, COM1_LCR
    mov al, 0x80
    out dx, al

    ; Set divisor to 1 (115200 baud)
    mov dx, COM1_DLL
    mov al, 1
    out dx, al
    mov dx, COM1_DLH
    xor al, al
    out dx, al

    ; 8 bits, no parity, one stop bit (8N1), disable DLAB
    mov dx, COM1_LCR
    mov al, 0x03
    out dx, al

    ; Enable FIFO, clear them, with 14-byte threshold
    mov dx, COM1_FCR
    mov al, 0xC7
    out dx, al

    ; Enable DTR, RTS, and OUT2 (required for interrupts)
    mov dx, COM1_MCR
    mov al, 0x0B
    out dx, al

    ret

; Print null-terminated string to serial port
; Input: rcx = pointer to string
serial_print:
    push rbx
    mov rbx, rcx

.print_loop:
    movzx eax, byte [rbx]
    test al, al
    jz .print_done

    ; Wait for transmit buffer to be empty
.wait_tx:
    mov dx, COM1_LSR
    in al, dx
    test al, 0x20       ; Check THR empty bit
    jz .wait_tx

    ; Send character
    mov dx, COM1_DATA
    mov al, [rbx]
    out dx, al

    inc rbx
    jmp .print_loop

.print_done:
    pop rbx
    ret

;; ==================== Port I/O ====================
;; These instructions have no high-level equivalent

global outb, outw, outd, inb, inw, ind

; void outb(uint16_t port, uint8_t value)
; Windows x64 ABI: port in cx, value in dl
outb:
    mov eax, edx
    mov dx, cx
    out dx, al
    ret

; void outw(uint16_t port, uint16_t value)
outw:
    mov eax, edx
    mov dx, cx
    out dx, ax
    ret

; void outd(uint16_t port, uint32_t value)
outd:
    mov eax, edx
    mov dx, cx
    out dx, eax
    ret

; uint8_t inb(uint16_t port)
inb:
    mov dx, cx
    in al, dx
    movzx eax, al
    ret

; uint16_t inw(uint16_t port)
inw:
    mov dx, cx
    in ax, dx
    movzx eax, ax
    ret

; uint32_t ind(uint16_t port)
ind:
    mov dx, cx
    in eax, dx
    ret

;; ==================== CPU Control ====================

global hlt, cli, sti, pause

; void hlt(void) - halt until interrupt
hlt:
    hlt
    ret

; void cli(void) - clear interrupt flag
cli:
    cli
    ret

; void sti(void) - set interrupt flag
sti:
    sti
    ret

; void pause(void) - spin-wait hint
pause:
    pause
    ret

;; ==================== Data ====================

section .data

boot_msg:
    db 10, 13
    db "==============================", 10, 13
    db "  netos nernel booted!", 10, 13
    db "==============================", 10, 13
    db 0
