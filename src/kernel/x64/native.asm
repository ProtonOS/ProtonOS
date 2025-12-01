; ProtonOS kernel - x64 native layer
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
;; Linker entry point that saves UEFI parameters, then calls korlib's EfiMain.

extern EfiMain  ; korlib's EfiMain

; EFI_STATUS EFIAPI EfiEntry(EFI_HANDLE ImageHandle, EFI_SYSTEM_TABLE* SystemTable)
; Windows x64 ABI: ImageHandle in rcx, SystemTable in rdx
global EfiEntry
EfiEntry:
    ; Save UEFI parameters to globals for later retrieval by managed code
    mov [rel g_uefi_image_handle], rcx
    mov [rel g_uefi_system_table], rdx

    ; Tail-call to korlib's EfiMain (parameters already in correct registers)
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

global read_cr0, write_cr0, read_cr2, read_cr3, write_cr3, read_cr4, write_cr4

; uint64_t read_cr0(void) - Read CR0
read_cr0:
    mov rax, cr0
    ret

; void write_cr0(uint64_t value) - Write CR0
; Windows x64 ABI: value in rcx
write_cr0:
    mov cr0, rcx
    ret

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

; uint64_t read_cr4(void) - Read CR4
read_cr4:
    mov rax, cr4
    ret

; void write_cr4(uint64_t value) - Write CR4
; Windows x64 ABI: value in rcx
write_cr4:
    mov cr4, rcx
    ret

;; ==================== CPUID ====================

global cpuid_ex

; void cpuid_ex(uint32_t leaf, uint32_t subleaf, uint32_t* eax, uint32_t* ebx, uint32_t* ecx, uint32_t* edx)
; Windows x64 ABI: leaf in ecx, subleaf in edx, eax* in r8, ebx* in r9, ecx* at [rsp+40], edx* at [rsp+48]
cpuid_ex:
    push rbx                ; rbx is callee-saved

    mov eax, ecx            ; leaf
    mov ecx, edx            ; subleaf (cpuid uses ecx for subleaf)
    cpuid                   ; eax/ebx/ecx/edx now contain results

    ; Store results
    mov [r8], eax           ; *eax_out = eax
    mov [r9], ebx           ; *ebx_out = ebx

    ; Get stack args (note: we pushed rbx, so offset is +8 from normal)
    mov r10, [rsp + 48]     ; ecx_out pointer
    mov [r10], ecx          ; *ecx_out = ecx
    mov r10, [rsp + 56]     ; edx_out pointer
    mov [r10], edx          ; *edx_out = edx

    pop rbx
    ret

;; ==================== XCR (Extended Control Registers) ====================

global xgetbv, xsetbv

; uint64_t xgetbv(uint32_t xcr) - Read extended control register
; Windows x64 ABI: xcr in ecx
xgetbv:
    xgetbv              ; Read XCR[ecx] into edx:eax
    shl rdx, 32
    or rax, rdx
    ret

; void xsetbv(uint32_t xcr, uint64_t value) - Write extended control register
; Windows x64 ABI: xcr in ecx, value in rdx
xsetbv:
    mov rax, rdx        ; Low 32 bits
    shr rdx, 32         ; High 32 bits
    xsetbv              ; Write edx:eax to XCR[ecx]
    ret

;; ==================== FPU/SSE Initialization ====================

global fninit

; void fninit(void) - Initialize x87 FPU
fninit:
    fninit
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
;; CpuContext structure layout (must match C# CpuContext):
;;   0x00: rax, 0x08: rbx, 0x10: rcx, 0x18: rdx
;;   0x20: rsi, 0x28: rdi, 0x30: rbp
;;   0x38: r8,  0x40: r9,  0x48: r10, 0x50: r11
;;   0x58: r12, 0x60: r13, 0x68: r14, 0x70: r15
;;   0x78: rip, 0x80: rsp, 0x88: rflags
;;   0x90: cs,  0x98: ss

; void switch_context(CpuContext* oldContext, CpuContext* newContext)
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

; void load_context(CpuContext* context)
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

;; ==================== PAL Context Restore ====================
;; Restore context from PAL CONTEXT structure (different layout from CpuContext)

; PAL CONTEXT structure layout:
;   0x00: ContextFlags (uint32)
;   0x04: SegCs (uint16), 0x06: SegDs, 0x08: SegEs, 0x0A: SegFs, 0x0C: SegGs, 0x0E: SegSs
;   0x10: Rip, 0x18: Rsp, 0x20: Rbp, 0x28: EFlags
;   0x30: Rax, 0x38: Rbx, 0x40: Rcx, 0x48: Rdx
;   0x50: Rsi, 0x58: Rdi
;   0x60: R8, 0x68: R9, 0x70: R10, 0x78: R11
;   0x80: R12, 0x88: R13, 0x90: R14, 0x98: R15

; void restore_pal_context(Context* ctx)
; Windows x64 ABI: ctx in rcx
; This function does not return - it jumps to the context's RIP
global restore_pal_context
restore_pal_context:
    ; Load rsp first
    mov rsp, [rcx + 0x18]

    ; Push the return address (RIP) onto stack
    mov rax, [rcx + 0x10]
    push rax

    ; Load all general purpose registers
    mov rax, [rcx + 0x30]
    mov rbx, [rcx + 0x38]
    ; rcx loaded last since we're using it
    mov rdx, [rcx + 0x48]
    mov rsi, [rcx + 0x50]
    mov rdi, [rcx + 0x58]
    mov rbp, [rcx + 0x20]
    mov r8,  [rcx + 0x60]
    mov r9,  [rcx + 0x68]
    mov r10, [rcx + 0x70]
    mov r11, [rcx + 0x78]
    mov r12, [rcx + 0x80]
    mov r13, [rcx + 0x88]
    mov r14, [rcx + 0x90]
    mov r15, [rcx + 0x98]

    ; Load RFLAGS (EFlags is 64-bit in our struct for alignment)
    push qword [rcx + 0x28]
    popfq

    ; Finally load rcx
    mov rcx, [rcx + 0x40]

    ; Jump to restored context (RIP was pushed onto stack)
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
global atomic_cmpxchg64, atomic_xchg64, atomic_add64

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

; long atomic_add64(long* ptr, long addend)
; Windows x64 ABI: ptr in rcx, addend in rdx
; Returns: original value at *ptr (before addition)
atomic_add64:
    mov rax, rdx
    lock xadd [rcx], rax    ; atomically add rdx to *rcx, original value in rax
    ret

; long atomic_cmpxchg64(long* ptr, long newVal, long comparand)
; Windows x64 ABI: ptr in rcx, newVal in rdx, comparand in r8
; Returns: original value at *ptr (if original == comparand, exchange occurred)
atomic_cmpxchg64:
    mov rax, r8             ; comparand goes in rax
    lock cmpxchg [rcx], rdx ; if *rcx == rax, *rcx = rdx; else rax = *rcx
    ret

; long atomic_xchg64(long* ptr, long newVal)
; Windows x64 ABI: ptr in rcx, newVal in rdx
; Returns: original value at *ptr
atomic_xchg64:
    mov rax, rdx
    lock xchg [rcx], rax    ; atomically exchange *rcx with rax
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

;; ==================== Register Access ====================
;; Needed for PAL stack bounds detection

global get_rsp

; ulong get_rsp()
; Returns: current RSP value
get_rsp:
    mov rax, rsp
    add rax, 8              ; adjust for return address pushed by call
    ret

;; ==================== Managed Exception Support ====================
;; Assembly support for NativeAOT/managed exception handling.
;; These functions handle context capture at throw site and restoration at catch site.

extern RhpThrowEx_Handler   ; C# exception dispatch handler
extern RhpThrowHwEx_Handler ; C# hardware exception handler
extern RhpRethrow_Handler   ; C# rethrow handler

; ExceptionContext structure layout (must match C# ExceptionContext):
;   0x00: Rip, 0x08: Rsp, 0x10: Rbp, 0x18: Rflags
;   0x20: Rax, 0x28: Rbx, 0x30: Rcx, 0x38: Rdx
;   0x40: Rsi, 0x48: Rdi
;   0x50: R8, 0x58: R9, 0x60: R10, 0x68: R11
;   0x70: R12, 0x78: R13, 0x80: R14, 0x88: R15
;   0x90: Cs (ushort), 0x92: Ss (ushort)
EXCEPTION_CONTEXT_SIZE equ 0x98

; void RhpThrowEx(void* exceptionObject)
; Called by compiler-generated code for: throw exception;
; Windows x64 ABI: exceptionObject in rcx
; This captures the full context at the throw site and calls the C# handler.
global RhpThrowEx
RhpThrowEx:
    ; Allocate space for ExceptionContext on stack
    sub rsp, EXCEPTION_CONTEXT_SIZE

    ; Capture context - save all registers
    ; Rip = return address (where throw instruction was)
    mov rax, [rsp + EXCEPTION_CONTEXT_SIZE]  ; return address
    mov [rsp + 0x00], rax

    ; Rsp = caller's RSP (after our stack allocation and return address)
    lea rax, [rsp + EXCEPTION_CONTEXT_SIZE + 8]
    mov [rsp + 0x08], rax

    ; Rbp
    mov [rsp + 0x10], rbp

    ; Rflags
    pushfq
    pop rax
    mov [rsp + 0x18], rax

    ; General purpose registers
    mov [rsp + 0x20], rax   ; Rax (already in rax from flags)
    mov [rsp + 0x28], rbx
    mov [rsp + 0x30], rcx   ; Rcx = exceptionObject (save it)
    mov [rsp + 0x38], rdx
    mov [rsp + 0x40], rsi
    mov [rsp + 0x48], rdi
    mov [rsp + 0x50], r8
    mov [rsp + 0x58], r9
    mov [rsp + 0x60], r10
    mov [rsp + 0x68], r11
    mov [rsp + 0x70], r12
    mov [rsp + 0x78], r13
    mov [rsp + 0x80], r14
    mov [rsp + 0x88], r15

    ; Segment registers
    mov ax, cs
    mov [rsp + 0x90], ax
    mov ax, ss
    mov [rsp + 0x92], ax

    ; Call C# handler: void RhpThrowEx_Handler(void* exceptionObject, ExceptionContext* context)
    ; rcx = exceptionObject (already there)
    ; rdx = pointer to context on stack
    mov rdx, rsp
    sub rsp, 32             ; shadow space
    call RhpThrowEx_Handler
    add rsp, 32

    ; If we return here, handler modified context to point to catch funclet
    ; NativeAOT funclets:
    ; - Are called with: RCX = exception object, RDX = frame pointer (RBP of faulting frame)
    ; - Return in RAX the address to continue execution at (after the try-catch block)
    ; We need to:
    ; 1. Call the funclet with proper args (already set in context by C# code)
    ; 2. Jump to the continuation address returned by the funclet

    ; Load handler address (funclet) and establisher frame
    mov rax, [rsp + 0x00]   ; new Rip = funclet address
    mov r11, [rsp + 0x08]   ; new Rsp = establisher frame RSP

    ; Load the funclet arguments from context (set by C# handler)
    mov rcx, [rsp + 0x30]   ; Rcx = exception object
    mov rdx, [rsp + 0x10]   ; Rdx = frame pointer (RBP) - establisher frame

    ; Load callee-saved registers that funclet might need from parent frame
    mov rbx, [rsp + 0x28]
    mov rbp, [rsp + 0x10]   ; RBP = establisher frame pointer
    mov r12, [rsp + 0x70]
    mov r13, [rsp + 0x78]
    mov r14, [rsp + 0x80]
    mov r15, [rsp + 0x88]

    ; Set up stack for funclet call
    ; Funclet expects to be called on the establisher frame's stack
    mov rsp, r11

    ; Ensure 16-byte stack alignment before call
    and rsp, ~0xF
    sub rsp, 32             ; shadow space for Windows x64 calling convention

    ; Call the funclet - it returns continuation address in RAX
    call rax

    ; RAX now contains the continuation address
    ; Clean up shadow space and continue
    add rsp, 32

    ; Jump to continuation address (where execution continues after the try-catch)
    jmp rax

; void RhpRethrow()
; Called by compiler-generated code for: throw; (rethrow current exception)
; Captures context and calls C# handler which retrieves current exception from TLS.
global RhpRethrow
RhpRethrow:
    ; Allocate space for ExceptionContext on stack
    sub rsp, EXCEPTION_CONTEXT_SIZE

    ; Capture context - save all registers
    ; Rip = return address (where rethrow instruction was)
    mov rax, [rsp + EXCEPTION_CONTEXT_SIZE]
    mov [rsp + 0x00], rax

    ; Rsp = caller's RSP
    lea rax, [rsp + EXCEPTION_CONTEXT_SIZE + 8]
    mov [rsp + 0x08], rax

    ; Save all registers (same as RhpThrowEx)
    mov [rsp + 0x10], rbp
    pushfq
    pop rax
    mov [rsp + 0x18], rax
    mov [rsp + 0x20], rax
    mov [rsp + 0x28], rbx
    mov [rsp + 0x30], rcx
    mov [rsp + 0x38], rdx
    mov [rsp + 0x40], rsi
    mov [rsp + 0x48], rdi
    mov [rsp + 0x50], r8
    mov [rsp + 0x58], r9
    mov [rsp + 0x60], r10
    mov [rsp + 0x68], r11
    mov [rsp + 0x70], r12
    mov [rsp + 0x78], r13
    mov [rsp + 0x80], r14
    mov [rsp + 0x88], r15
    mov ax, cs
    mov [rsp + 0x90], ax
    mov ax, ss
    mov [rsp + 0x92], ax

    ; Call C# handler: void RhpRethrow_Handler(ExceptionContext* context)
    ; rcx = pointer to context
    mov rcx, rsp
    sub rsp, 32             ; shadow space
    call RhpRethrow_Handler
    add rsp, 32

    ; If we return, handler modified context - call funclet like RhpThrowEx
    mov rax, [rsp + 0x00]   ; funclet address
    mov r11, [rsp + 0x08]   ; establisher frame RSP
    mov rcx, [rsp + 0x30]   ; exception object
    mov rdx, [rsp + 0x10]   ; frame pointer (RBP)
    mov rbx, [rsp + 0x28]
    mov rbp, [rsp + 0x10]
    mov r12, [rsp + 0x70]
    mov r13, [rsp + 0x78]
    mov r14, [rsp + 0x80]
    mov r15, [rsp + 0x88]
    mov rsp, r11
    and rsp, ~0xF
    sub rsp, 32
    call rax
    add rsp, 32
    jmp rax

; void RhpThrowHwEx(uint exceptionCode, void* faultingIP)
; Called for hardware exceptions (null ref, divide by zero, etc.)
; Windows x64 ABI: exceptionCode in ecx, faultingIP in rdx
; Captures context and calls C# handler which creates appropriate exception object.
global RhpThrowHwEx
RhpThrowHwEx:
    ; Save exception code and faulting IP
    push rdx                ; save faultingIP
    push rcx                ; save exceptionCode (as 64-bit)

    ; Allocate space for ExceptionContext on stack
    sub rsp, EXCEPTION_CONTEXT_SIZE

    ; Capture context
    ; Rip = faulting IP (passed in rdx, now saved above)
    mov rax, [rsp + EXCEPTION_CONTEXT_SIZE + 8]  ; faultingIP from stack
    mov [rsp + 0x00], rax

    ; Rsp = caller's RSP (after our allocations)
    lea rax, [rsp + EXCEPTION_CONTEXT_SIZE + 24]  ; +8 for each push + return addr
    mov [rsp + 0x08], rax

    ; Save all registers
    mov [rsp + 0x10], rbp
    pushfq
    pop rax
    mov [rsp + 0x18], rax
    mov [rsp + 0x20], rax
    mov [rsp + 0x28], rbx
    mov rax, [rsp + EXCEPTION_CONTEXT_SIZE]       ; get original ecx (exception code)
    mov [rsp + 0x30], rax
    mov rax, [rsp + EXCEPTION_CONTEXT_SIZE + 8]   ; get original rdx (faultingIP)
    mov [rsp + 0x38], rax
    mov [rsp + 0x40], rsi
    mov [rsp + 0x48], rdi
    mov [rsp + 0x50], r8
    mov [rsp + 0x58], r9
    mov [rsp + 0x60], r10
    mov [rsp + 0x68], r11
    mov [rsp + 0x70], r12
    mov [rsp + 0x78], r13
    mov [rsp + 0x80], r14
    mov [rsp + 0x88], r15
    mov ax, cs
    mov [rsp + 0x90], ax
    mov ax, ss
    mov [rsp + 0x92], ax

    ; Call C# handler: void RhpThrowHwEx_Handler(uint exceptionCode, ExceptionContext* context)
    ; ecx = exception code (32-bit)
    mov ecx, [rsp + EXCEPTION_CONTEXT_SIZE]       ; get exception code
    ; rdx = pointer to context
    mov rdx, rsp
    sub rsp, 32             ; shadow space
    call RhpThrowHwEx_Handler
    add rsp, 32

    ; If we return, handler modified context - call funclet like RhpThrowEx
    mov rax, [rsp + 0x00]   ; funclet address
    mov r11, [rsp + 0x08]   ; establisher frame RSP
    mov rcx, [rsp + 0x30]   ; exception object (set by C# handler)
    mov rdx, [rsp + 0x10]   ; frame pointer (RBP)
    mov rbx, [rsp + 0x28]
    mov rbp, [rsp + 0x10]
    mov r12, [rsp + 0x70]
    mov r13, [rsp + 0x78]
    mov r14, [rsp + 0x80]
    mov r15, [rsp + 0x88]
    mov rsp, r11
    and rsp, ~0xF
    sub rsp, 32
    call rax
    add rsp, 32
    jmp rax

; void RhpCallCatchFunclet(void* exceptionObject, void* handlerAddress, void* framePointer)
; Transfer control to a catch funclet with proper setup
; Windows x64 ABI: exceptionObject in rcx, handlerAddress in rdx, framePointer in r8
; The catch funclet expects: rcx = exception object, rdx = frame pointer
global RhpCallCatchFunclet
RhpCallCatchFunclet:
    ; Set up for funclet call
    mov rax, rdx            ; handler address
    mov rdx, r8             ; frame pointer goes in rdx for funclet
    ; rcx already has exception object
    jmp rax                 ; tail-call to funclet

; void RhpCallFinallyFunclet(void* handlerAddress, void* framePointer)
; Transfer control to a finally funclet
; Windows x64 ABI: handlerAddress in rcx, framePointer in rdx
global RhpCallFinallyFunclet
RhpCallFinallyFunclet:
    ; Set up for funclet call
    mov rax, rcx            ; handler address
    ; rdx already has frame pointer
    call rax                ; call funclet (it returns)
    ret

;; ==================== Context Capture for GC ====================
;; Captures current thread context for GC stack root enumeration

; void capture_context(ExceptionContext* context)
; Captures all registers into the provided ExceptionContext structure.
; Windows x64 ABI: context pointer in rcx
; ExceptionContext layout (offsets in bytes):
;   0x00: Rip
;   0x08: Rsp
;   0x10: Rbp
;   0x18: Rflags
;   0x20: Rax
;   0x28: Rbx
;   0x30: Rcx
;   0x38: Rdx
;   0x40: Rsi
;   0x48: Rdi
;   0x50: R8
;   0x58: R9
;   0x60: R10
;   0x68: R11
;   0x70: R12
;   0x78: R13
;   0x80: R14
;   0x88: R15
;   0x90: Cs
;   0x92: Ss
global capture_context
capture_context:
    ; Rip = return address (instruction after call)
    mov rax, [rsp]
    mov [rcx + 0x00], rax

    ; Rsp = caller's RSP (after return address)
    lea rax, [rsp + 8]
    mov [rcx + 0x08], rax

    ; Rbp
    mov [rcx + 0x10], rbp

    ; Rflags
    pushfq
    pop rax
    mov [rcx + 0x18], rax

    ; General purpose registers
    mov [rcx + 0x20], rax   ; Rax (has flags, but that's fine - caller didn't rely on it)
    mov [rcx + 0x28], rbx
    mov [rcx + 0x30], rcx   ; Rcx (context pointer, but we're done with it)
    mov [rcx + 0x38], rdx
    mov [rcx + 0x40], rsi
    mov [rcx + 0x48], rdi
    mov [rcx + 0x50], r8
    mov [rcx + 0x58], r9
    mov [rcx + 0x60], r10
    mov [rcx + 0x68], r11
    mov [rcx + 0x70], r12
    mov [rcx + 0x78], r13
    mov [rcx + 0x80], r14
    mov [rcx + 0x88], r15

    ; Segment registers
    mov ax, cs
    mov [rcx + 0x90], ax
    mov ax, ss
    mov [rcx + 0x92], ax

    ret
