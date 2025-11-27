; netos nernel - x64 native layer
; Minimal CPU intrinsics that cannot be expressed in C#
;
; Note: UEFI entry point (EfiMain) is provided by bflat's zerolib.
; This file provides CPU-level primitives that cannot be expressed in C#.

BITS 64
DEFAULT REL

section .text

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
