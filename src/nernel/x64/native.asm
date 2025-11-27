; netos nernel - x64 native layer
; Provides UEFI entry point hook and CPU primitives for the kernel.

BITS 64
DEFAULT REL

section .data

;; UEFI parameters - stored for later retrieval by managed code
global g_uefi_image_handle, g_uefi_system_table
g_uefi_image_handle: dq 0
g_uefi_system_table:  dq 0

section .text

;; ==================== UEFI Entry Point ====================
;; Linker entry point that saves UEFI parameters, then calls zerolib's EfiMain.

extern EfiMain  ; zerolib's EfiMain

; EFI_STATUS EFIAPI EfiEntry(EFI_HANDLE ImageHandle, EFI_SYSTEM_TABLE* SystemTable)
; Windows x64 ABI: ImageHandle in rcx, SystemTable in rdx
global EfiEntry
EfiEntry:
    ; Save UEFI parameters to globals for later retrieval by managed code
    mov [rel g_uefi_image_handle], rcx
    mov [rel g_uefi_system_table], rdx

    ; Tail-call to zerolib's EfiMain (parameters already in correct registers)
    jmp EfiMain

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

;; ==================== Descriptor Tables ====================

global lgdt, lidt, ltr

; void lgdt(void* gdtPtr) - Load Global Descriptor Table
; Windows x64 ABI: gdtPtr in rcx
lgdt:
    lgdt [rcx]
    ret

; void reload_segments(uint16_t codeSelector, uint16_t dataSelector)
; Reload CS via far return, reload data segments directly
; Windows x64 ABI: codeSelector in cx, dataSelector in dx
global reload_segments
reload_segments:
    ; Save data selector
    mov ax, dx

    ; Reload data segment registers
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax

    ; Reload CS via far return
    ; Push new CS and return address, then retfq
    pop rax                 ; Get return address
    push rcx                ; Push new CS
    push rax                ; Push return address
    retfq                   ; Far return to reload CS

; void lidt(void* idtPtr) - Load Interrupt Descriptor Table
; Windows x64 ABI: idtPtr in rcx
lidt:
    lidt [rcx]
    ret

; void ltr(uint16_t selector) - Load Task Register
; Windows x64 ABI: selector in cx
ltr:
    ltr cx
    ret

;; ==================== Control Registers ====================

global read_cr2

; uint64_t read_cr2(void) - Read CR2 (page fault linear address)
read_cr2:
    mov rax, cr2
    ret

;; ==================== CPU Control ====================

global hlt, cli, sti, pause, int3

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

; void int3(void) - trigger breakpoint exception
int3:
    int3
    ret

;; ==================== Interrupt Stubs ====================
;; ISR stubs save all registers, call managed handler, restore, and iretq.
;; Some interrupts push an error code, others don't - we normalize by pushing 0.

extern InterruptDispatch

; Macro for ISR without error code (pushes dummy 0)
%macro ISR_NOERRCODE 1
global isr%1
isr%1:
    push qword 0            ; Dummy error code
    push qword %1           ; Interrupt number
    jmp isr_common
%endmacro

; Macro for ISR with error code (CPU already pushed it)
%macro ISR_ERRCODE 1
global isr%1
isr%1:
    push qword %1           ; Interrupt number
    jmp isr_common
%endmacro

; Common ISR handler - saves state, calls C#, restores, iretq
isr_common:
    ; Save all general-purpose registers
    push rax
    push rbx
    push rcx
    push rdx
    push rsi
    push rdi
    push rbp
    push r8
    push r9
    push r10
    push r11
    push r12
    push r13
    push r14
    push r15

    ; Save segment registers (ds, es)
    mov ax, ds
    push rax
    mov ax, es
    push rax

    ; Load kernel data segment
    mov ax, 0x10            ; GdtSelectors.KernelData
    mov ds, ax
    mov es, ax

    ; Call managed interrupt dispatcher
    ; Windows x64 ABI: first arg in rcx = pointer to interrupt frame
    mov rcx, rsp
    sub rsp, 32             ; Shadow space
    call InterruptDispatch
    add rsp, 32

    ; Restore segment registers
    pop rax
    mov es, ax
    pop rax
    mov ds, ax

    ; Restore general-purpose registers
    pop r15
    pop r14
    pop r13
    pop r12
    pop r11
    pop r10
    pop r9
    pop r8
    pop rbp
    pop rdi
    pop rsi
    pop rdx
    pop rcx
    pop rbx
    pop rax

    ; Remove interrupt number and error code from stack
    add rsp, 16

    ; Return from interrupt
    iretq

;; Generate all 256 ISR stubs
;; Exceptions 0-31, IRQs 32-47, software interrupts 48-255

; CPU Exceptions (0-31)
; Error code: 8, 10, 11, 12, 13, 14, 17, 21, 29, 30
ISR_NOERRCODE 0     ; Divide by zero
ISR_NOERRCODE 1     ; Debug
ISR_NOERRCODE 2     ; NMI
ISR_NOERRCODE 3     ; Breakpoint
ISR_NOERRCODE 4     ; Overflow
ISR_NOERRCODE 5     ; Bound range exceeded
ISR_NOERRCODE 6     ; Invalid opcode
ISR_NOERRCODE 7     ; Device not available
ISR_ERRCODE   8     ; Double fault
ISR_NOERRCODE 9     ; Coprocessor segment overrun (legacy)
ISR_ERRCODE   10    ; Invalid TSS
ISR_ERRCODE   11    ; Segment not present
ISR_ERRCODE   12    ; Stack segment fault
ISR_ERRCODE   13    ; General protection fault
ISR_ERRCODE   14    ; Page fault
ISR_NOERRCODE 15    ; Reserved
ISR_NOERRCODE 16    ; x87 FPU error
ISR_ERRCODE   17    ; Alignment check
ISR_NOERRCODE 18    ; Machine check
ISR_NOERRCODE 19    ; SIMD floating point
ISR_NOERRCODE 20    ; Virtualization exception
ISR_ERRCODE   21    ; Control protection exception
ISR_NOERRCODE 22    ; Reserved
ISR_NOERRCODE 23    ; Reserved
ISR_NOERRCODE 24    ; Reserved
ISR_NOERRCODE 25    ; Reserved
ISR_NOERRCODE 26    ; Reserved
ISR_NOERRCODE 27    ; Reserved
ISR_NOERRCODE 28    ; Hypervisor injection
ISR_ERRCODE   29    ; VMM communication exception
ISR_ERRCODE   30    ; Security exception
ISR_NOERRCODE 31    ; Reserved

; IRQs and software interrupts (32-255)
%assign i 32
%rep 224
ISR_NOERRCODE i
%assign i i+1
%endrep

;; ISR table for IDT setup
section .data
isr_table:
%assign i 0
%rep 256
    dq isr%+i
%assign i i+1
%endrep

section .text

;; Function to get ISR table address (for C# interop)
global get_isr_table
get_isr_table:
    lea rax, [rel isr_table]
    ret

;; ==================== UEFI Parameter Access ====================
;; Functions to retrieve UEFI parameters saved by EfiMainHook

; void* get_uefi_system_table(void)
global get_uefi_system_table
get_uefi_system_table:
    mov rax, [rel g_uefi_system_table]
    ret

; void* get_uefi_image_handle(void)
global get_uefi_image_handle
get_uefi_image_handle:
    mov rax, [rel g_uefi_image_handle]
    ret
