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

global read_cr2, read_cr3, write_cr3

; uint64_t read_cr2(void) - Read CR2 (page fault linear address)
read_cr2:
    mov rax, cr2
    ret

; uint64_t read_cr3(void) - Read CR3 (page table base)
read_cr3:
    mov rax, cr3
    ret

; void write_cr3(uint64_t value) - Write CR3 (switch page tables)
; Windows x64 ABI: value in rcx
write_cr3:
    mov cr3, rcx
    ret

;; ==================== TLB ====================

global invlpg

; void invlpg(uint64_t virtualAddress) - Invalidate TLB entry
; Windows x64 ABI: virtualAddress in rcx
invlpg:
    invlpg [rcx]
    ret

;; ==================== MSR (Model Specific Registers) ====================

global rdmsr, wrmsr

; uint64_t rdmsr(uint32_t msr) - Read MSR
; Windows x64 ABI: msr in ecx (already there!)
rdmsr:
    rdmsr               ; Result in edx:eax
    shl rdx, 32
    or rax, rdx
    ret

; void wrmsr(uint32_t msr, uint64_t value) - Write MSR
; Windows x64 ABI: msr in ecx, value in rdx
wrmsr:
    mov rax, rdx        ; Low 32 bits
    shr rdx, 32         ; High 32 bits
    wrmsr
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

;; ==================== Context Switching ====================
;; KernelCpuContext structure layout (must match C# KernelCpuContext):
;;   0x00: rax, 0x08: rbx, 0x10: rcx, 0x18: rdx
;;   0x20: rsi, 0x28: rdi, 0x30: rbp
;;   0x38: r8,  0x40: r9,  0x48: r10, 0x50: r11
;;   0x58: r12, 0x60: r13, 0x68: r14, 0x70: r15
;;   0x78: rip, 0x80: rsp, 0x88: rflags
;;   0x90: cs,  0x98: ss

; void switch_context(KernelCpuContext* oldContext, KernelCpuContext* newContext)
; Windows x64 ABI: oldContext in rcx, newContext in rdx
; Saves current context to oldContext, loads newContext
global switch_context
switch_context:
    ; Save current context to oldContext (rcx)
    mov [rcx + 0x00], rax
    mov [rcx + 0x08], rbx
    ; rcx will be saved after we're done using it
    mov [rcx + 0x18], rdx
    mov [rcx + 0x20], rsi
    mov [rcx + 0x28], rdi
    mov [rcx + 0x30], rbp
    mov [rcx + 0x38], r8
    mov [rcx + 0x40], r9
    mov [rcx + 0x48], r10
    mov [rcx + 0x50], r11
    mov [rcx + 0x58], r12
    mov [rcx + 0x60], r13
    mov [rcx + 0x68], r14
    mov [rcx + 0x70], r15

    ; Save return address as RIP
    mov rax, [rsp]          ; Return address is at top of stack
    mov [rcx + 0x78], rax

    ; Save RSP (after return, so +8)
    lea rax, [rsp + 8]
    mov [rcx + 0x80], rax

    ; Save RFLAGS
    pushfq
    pop rax
    mov [rcx + 0x88], rax

    ; Save CS and SS
    mov ax, cs
    movzx rax, ax
    mov [rcx + 0x90], rax
    mov ax, ss
    movzx rax, ax
    mov [rcx + 0x98], rax

    ; Now save rcx (oldContext pointer)
    mov rax, rcx
    mov [rcx + 0x10], rax

    ; Load new context from newContext (rdx)
    ; First load rsp so we have a valid stack
    mov rsp, [rdx + 0x80]

    ; Push the return address (new RIP) onto new stack
    mov rax, [rdx + 0x78]
    push rax

    ; Load all registers from new context
    mov rax, [rdx + 0x00]
    mov rbx, [rdx + 0x08]
    mov rcx, [rdx + 0x10]
    ; rdx loaded last since we're using it
    mov rsi, [rdx + 0x20]
    mov rdi, [rdx + 0x28]
    mov rbp, [rdx + 0x30]
    mov r8,  [rdx + 0x38]
    mov r9,  [rdx + 0x40]
    mov r10, [rdx + 0x48]
    mov r11, [rdx + 0x50]
    mov r12, [rdx + 0x58]
    mov r13, [rdx + 0x60]
    mov r14, [rdx + 0x68]
    mov r15, [rdx + 0x70]

    ; Load RFLAGS
    push qword [rdx + 0x88]
    popfq

    ; Finally load rdx
    mov rdx, [rdx + 0x18]

    ; Return to new context (RIP was pushed onto stack)
    ret

; void load_context(KernelCpuContext* context)
; Windows x64 ABI: context in rcx
; Loads context without saving (for first thread switch)
global load_context
load_context:
    ; Load rsp first
    mov rsp, [rcx + 0x80]

    ; Push the return address (RIP) onto stack
    mov rax, [rcx + 0x78]
    push rax

    ; Load all registers
    mov rax, [rcx + 0x00]
    mov rbx, [rcx + 0x08]
    ; rcx loaded last
    mov rdx, [rcx + 0x18]
    mov rsi, [rcx + 0x20]
    mov rdi, [rcx + 0x28]
    mov rbp, [rcx + 0x30]
    mov r8,  [rcx + 0x38]
    mov r9,  [rcx + 0x40]
    mov r10, [rcx + 0x48]
    mov r11, [rcx + 0x50]
    mov r12, [rcx + 0x58]
    mov r13, [rcx + 0x60]
    mov r14, [rcx + 0x68]
    mov r15, [rcx + 0x70]

    ; Load RFLAGS
    push qword [rcx + 0x88]
    popfq

    ; Finally load rcx
    mov rcx, [rcx + 0x10]

    ; Jump to new context
    ret

;; ==================== Memory Barriers ====================
;; CPU memory fence instructions

global mfence

; void mfence(void) - Full memory fence
; Serializes all memory operations (loads and stores)
mfence:
    mfence
    ret

;; ==================== Atomic Operations ====================
;; Lock-prefixed instructions for thread-safe operations

global atomic_cmpxchg32, atomic_xchg32, atomic_add32

; int atomic_add32(int* ptr, int addend)
; Windows x64 ABI: ptr in rcx, addend in edx
; Returns: original value at *ptr (before addition)
atomic_add32:
    mov eax, edx
    lock xadd [rcx], eax    ; atomically add edx to *rcx, original value in eax
    ret

; int atomic_cmpxchg32(int* ptr, int newVal, int comparand)
; Windows x64 ABI: ptr in rcx, newVal in edx, comparand in r8d
; Returns: original value at *ptr (if original == comparand, exchange occurred)
atomic_cmpxchg32:
    mov eax, r8d            ; comparand goes in eax
    lock cmpxchg [rcx], edx ; if *rcx == eax, *rcx = edx; else eax = *rcx
    ret

; int atomic_xchg32(int* ptr, int newVal)
; Windows x64 ABI: ptr in rcx, newVal in edx
; Returns: original value at *ptr
atomic_xchg32:
    mov eax, edx
    lock xchg [rcx], eax    ; atomically exchange *rcx with eax
    ret

;; ==================== Memory Operations ====================
;; Required by C# compiler for struct initialization

global memset, memcpy

; void* memset(void* dest, int c, size_t count)
; Windows x64 ABI: dest in rcx, c in edx, count in r8
; Returns: dest
memset:
    push rdi
    mov rax, rcx            ; save dest for return value
    mov rdi, rcx            ; dest for stosb
    mov rcx, r8             ; count
    mov r8, rax             ; save dest again (rcx now has count)
    mov rax, rdx            ; fill byte for stosb
    rep stosb
    mov rax, r8             ; return original dest
    pop rdi
    ret

; void* memcpy(void* dest, const void* src, size_t count)
; Windows x64 ABI: dest in rcx, src in rdx, count in r8
; Returns: dest
memcpy:
    push rdi
    push rsi
    mov rax, rcx            ; save dest for return
    mov rdi, rcx            ; dest
    mov rsi, rdx            ; src
    mov rcx, r8             ; count
    rep movsb
    pop rsi
    pop rdi
    ret
