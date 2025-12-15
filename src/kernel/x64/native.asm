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

;; ==================== Extended State Save/Restore ====================
;; FXSAVE/FXRSTOR - Save/restore legacy x87/SSE state (512 bytes, 16-byte aligned)
;; XSAVE/XRSTOR - Save/restore extended state including AVX (variable size, 64-byte aligned)

global fxsave, fxrstor, xsave, xrstor

; void fxsave(void* area) - Save FPU/SSE state to 512-byte area (16-byte aligned)
; Windows x64 ABI: area in rcx
fxsave:
    fxsave [rcx]
    ret

; void fxrstor(void* area) - Restore FPU/SSE state from 512-byte area (16-byte aligned)
; Windows x64 ABI: area in rcx
fxrstor:
    fxrstor [rcx]
    ret

; void xsave(void* area, uint64_t mask) - Save extended state (64-byte aligned)
; Windows x64 ABI: area in rcx, mask in rdx
; mask specifies which state components to save (XCR0 subset)
xsave:
    mov rax, rdx        ; Low 32 bits of mask
    shr rdx, 32         ; High 32 bits of mask
    xsave [rcx]         ; Save state components specified by edx:eax
    ret

; void xrstor(void* area, uint64_t mask) - Restore extended state (64-byte aligned)
; Windows x64 ABI: area in rcx, mask in rdx
; mask specifies which state components to restore (XCR0 subset)
xrstor:
    mov rax, rdx        ; Low 32 bits of mask
    shr rdx, 32         ; High 32 bits of mask
    xrstor [rcx]        ; Restore state components specified by edx:eax
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

;; ==================== TSC and Flags ====================

global rdtsc_native, read_flags

; uint64_t rdtsc_native(void) - Read Time Stamp Counter
; Returns 64-bit TSC value
rdtsc_native:
    rdtsc               ; Result in edx:eax
    shl rdx, 32
    or rax, rdx
    ret

; uint64_t read_flags(void) - Read RFLAGS register
; Returns current RFLAGS value
read_flags:
    pushfq              ; Push RFLAGS onto stack
    pop rax             ; Pop into return register
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

; RhpStackProbe - Stack probe for large stack allocations
; Windows x64 ABI: probe size in rax
; Must touch each page to avoid guard page violations
global RhpStackProbe
RhpStackProbe:
    ; rax contains the number of bytes to allocate
    ; We need to touch each page from rsp down to rsp-rax
    ; On Windows, page size is 4096 (0x1000)

    ; Preserve rax for the caller (will be subtracted from rsp by caller)
    push rax
    push rcx

    ; Start probing from current rsp
    mov rcx, rsp
    sub rcx, 8              ; Account for pushed rax

.probe_loop:
    sub rcx, 0x1000         ; Move down one page
    test [rcx], eax         ; Touch the page (reading is enough)
    sub rax, 0x1000
    ja .probe_loop          ; Continue if more pages to probe

    pop rcx
    pop rax
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
    ; Save context pointer in r15 (callee-saved) for verification after call
    mov r15, rsp
    sub rsp, 32             ; shadow space
    call RhpThrowEx_Handler
    add rsp, 32

    ; DEBUG: Verify RSP = saved context pointer
    cmp rsp, r15
    je .rsp_ok
    ; RSP doesn't match! Trigger breakpoint for debugging
    int3
.rsp_ok:

    ; Save context pointer again for second verification after reads
    mov r14, rsp

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

    ; DEBUG: Verify we're reading from the right context
    ; R14 should equal RSP (both should be context pointer)
    cmp r14, rsp
    je .ctx_ok
    int3                    ; Context pointer changed!
.ctx_ok:

    ; DEBUG: Verify R11 is reasonable (not parent frame pointer which indicates wrong context)
    ; If R11 equals the context address, something is very wrong (reading wrong field)
    cmp r11, r14
    jne .rsp_value_ok
    int3                    ; R11 == context address, wrong!
.rsp_value_ok:

    ; DEBUG: Verify RAX is a valid code address (high bits should be 0x00000002 for JIT code)
    mov r8, rax
    shr r8, 32
    cmp r8d, 2
    je .rax_ok
    ; RAX doesn't look like JIT code address, might be reading wrong field
    int3
.rax_ok:

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

    ; Set up stack for handler
    ; For inline handlers (not funclets), context->Rsp already points to return address
    ; Handler's RET will pop the return address and return to the original caller
    mov rsp, r11

    ; DEBUG: Before jmp, print the values we're using
    ; We'll use int 0xF0 as a special debug marker that our exception handler can recognize
    ; Actually, let's just do a simple validation: verify [rsp] contains a JIT code address
    mov r8, [rsp]        ; Load the return address
    shr r8, 32
    cmp r8d, 2           ; Should be 0x00000002xxxxxxxx
    je .ret_addr_ok
    ; Return address doesn't look like JIT code - BAD!
    int3
.ret_addr_ok:

    ; Jump directly to handler - it will RET to the original caller
    ; The C# code has set up RSP to point at the return address
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

    ; If we return, handler modified context - jump to handler like RhpThrowEx
    mov rax, [rsp + 0x00]   ; handler address
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
    ; Jump directly to handler - it will RET to the original caller
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

;; ==================== Finally Handler Support ====================
;; Calls a finally handler with proper frame setup.
;; The finally handler is an inline handler (not a funclet) that expects
;; RBP to be set up to access the function's locals.
;; The handler ends with 'endfinally' which just does 'ret'.

; void call_finally_handler(ulong handlerAddress, ulong framePointer)
; Windows x64 ABI: handlerAddress in rcx, framePointer in rdx
;
; The finally handler accesses locals via [rbp-X], so we need to set
; RBP = framePointer. The handler ends with 'ret', so we just call it.
global call_finally_handler
call_finally_handler:
    ; Save callee-saved registers we'll modify
    push rbp
    push rbx

    ; Save arguments
    mov rax, rcx        ; handler address
    mov rbx, rdx        ; frame pointer

    ; Set RBP to the original function's frame pointer
    ; This allows the handler to access locals via [rbp-X]
    mov rbp, rbx

    ; Call the finally handler
    ; The handler code does:
    ;   <handler body accessing [rbp-X]>
    ;   ret
    ; So it just returns to us
    call rax

    ; Handler has returned via 'ret'
    ; Restore callee-saved registers
    pop rbx
    pop rbp

    ret

;; =============================================================================
;; call_filter_funclet - Call a filter funclet during exception handling
;; =============================================================================
;; Filter funclets evaluate the condition in "catch when (condition)".
;; The filter receives the exception object and returns:
;;   0 = EXCEPTION_CONTINUE_SEARCH (don't handle)
;;   1 = EXCEPTION_EXECUTE_HANDLER (handle this exception)
;;
;; The funclet prolog is: push rbp; mov rbp, rdx (rdx = parent frame pointer)
;; So we pass: rcx = exception object, rdx = parent frame pointer

; int call_filter_funclet(ulong filterAddress, ulong framePointer, ulong exceptionObject)
; Windows x64 ABI: filterAddress in rcx, framePointer in rdx, exceptionObject in r8
; Returns: filter result in eax (0 or 1)
global call_filter_funclet
call_filter_funclet:
    ; Save callee-saved registers we'll modify
    push rbp
    push rbx
    push rdi

    ; Save arguments
    mov rax, rcx        ; filter address
    mov rbx, rdx        ; frame pointer
    mov rdi, r8         ; exception object

    ; Set up arguments for the filter funclet call:
    ; The funclet prolog does: push rbp; mov rbp, rdx
    ; So rdx must contain the parent frame pointer
    ; rcx should contain the exception object (first param to filter)
    mov rdx, rbx        ; rdx = parent frame pointer for funclet prolog
    mov rcx, rdi        ; rcx = exception object (first parameter)

    ; Call the filter funclet
    ; The filter code does:
    ;   push rbp
    ;   mov rbp, rdx        ; parent frame pointer
    ;   ... evaluate condition using exception in rcx ...
    ;   ... stores result in eax (0 or 1) ...
    ;   endfilter (pops rbp and returns)
    call rax

    ; Result is in eax (filter funclet returns 0 or 1)

    ; Restore callee-saved registers
    pop rdi
    pop rbx
    pop rbp

    ret

;; ==================== SMP AP Trampoline ====================
;; Real mode trampoline code for Application Processor startup.
;; This code is copied to low memory (0x90000 = 576KB) by the BSP before sending SIPI.
;; APs execute this code in real mode, transition through protected mode to long mode,
;; and finally call the C# AP entry point.
;;
;; IMPORTANT: All data access uses ABSOLUTE addresses (0x90000 + offset) because
;; RIP-relative addressing doesn't work after copying to 0x90000.
;; The startup data is embedded within the trampoline at a known offset.
;;
;; Address choice: Must be below 1MB for SIPI (8-bit vector = page number).
;; Page allocator uses ~0x1000-0xC000 for page tables, so 0x90000 is safe.

;; Constants for trampoline memory layout
AP_TRAMPOLINE_BASE equ 0x90000

section .text

;; Export symbols for trampoline boundaries
global ap_trampoline_start, ap_trampoline_end

;; The trampoline code itself - will be copied to 0x7000
;; Note: This is 64-bit code section but we switch to 16-bit for the trampoline
BITS 16
align 4096  ; Align to page boundary for easier copying

ap_trampoline_start:
    ; ---- Real Mode (16-bit) ----
    ; We start here after SIPI. CS:IP = 0x9000:0x0000 = 0x90000
    cli
    cld

    ; Set up segments for addressing trampoline at 0x90000
    ; DS = 0x9000 so that DS:offset = 0x90000 + offset
    ; This is necessary because 16-bit offsets can't reach 0x90000 directly
    mov ax, 0x9000
    mov ds, ax
    mov es, ax
    ; Set up stack just below trampoline at 0x90000
    ; SS=0x8000, SP=0xFFF0 -> effective address = 0x80000 + 0xFFF0 = 0x8FFF0
    mov ax, 0x8000
    mov ss, ax
    mov sp, 0xFFF0

    ; DEBUG: Output '1' to serial port to show we started
    mov dx, 0x3F8
    mov al, '1'
    out dx, al

    ; Data is at fixed offset from trampoline start
    ; With DS = 0x9000, DS:offset addresses 0x90000 + offset
    ; So we just use the offset from trampoline start
    mov ebx, (ap_trampoline_data - ap_trampoline_start)

    ; DEBUG: Output '2' to show we calculated data address
    mov dx, 0x3F8
    mov al, '2'
    out dx, al

    ; Load CR3 from startup data (page tables set up by BSP)
    ; ebx contains offset, DS:ebx gives us the actual data
    mov eax, [ebx]          ; Load low 32 bits of CR3
    mov cr3, eax

    ; DEBUG: Output '3' to show CR3 loaded
    mov dx, 0x3F8
    mov al, '3'
    out dx, al

    ; Enable PAE (bit 5 of CR4)
    mov eax, cr4
    or eax, (1 << 5)        ; PAE
    mov cr4, eax

    ; DEBUG: Output '4' to show PAE enabled
    mov dx, 0x3F8
    mov al, '4'
    out dx, al

    ; Load temporary GDT for protected mode
    ; DS = 0x9000, so DS:offset addresses 0x90000 + offset
    lgdt [(ap_trampoline_gdt_ptr - ap_trampoline_start)]

    ; DEBUG: Output '5' to show GDT loaded
    mov dx, 0x3F8
    mov al, '5'
    out dx, al

    ; Enable protected mode (bit 0 of CR0)
    mov eax, cr0
    or eax, 1
    mov cr0, eax

    ; DEBUG: Output '6' to show protected mode enabled
    mov dx, 0x3F8
    mov al, '6'
    out dx, al

    ; Far jump to 32-bit protected mode code
    jmp dword 0x18:(AP_TRAMPOLINE_BASE + (ap_trampoline_32 - ap_trampoline_start))

BITS 32
ap_trampoline_32:
    ; ---- Protected Mode (32-bit) ----

    ; DEBUG: Output '7' to show we're in protected mode
    mov dx, 0x3F8
    mov al, '7'
    out dx, al

    ; Set up 32-bit data segments
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax

    ; DEBUG: Output '8' to show segments loaded
    mov dx, 0x3F8
    mov al, '8'
    out dx, al

    ; Enable long mode in IA32_EFER MSR (bit 8 = LME)
    mov ecx, 0xC0000080     ; IA32_EFER MSR
    rdmsr
    or eax, (1 << 8)        ; LME (Long Mode Enable)
    wrmsr

    ; DEBUG: Output '9' to show LME set
    mov dx, 0x3F8
    mov al, '9'
    out dx, al

    ; Enable paging (bit 31 of CR0) - this activates long mode
    mov eax, cr0
    or eax, (1 << 31)
    mov cr0, eax

    ; DEBUG: Output 'A' to show paging enabled
    mov dx, 0x3F8
    mov al, 'A'
    out dx, al

    ; Far jump to 64-bit long mode code
    jmp dword 0x08:(AP_TRAMPOLINE_BASE + (ap_trampoline_64 - ap_trampoline_start))

BITS 64
ap_trampoline_64:
    ; ---- Long Mode (64-bit) ----
    ; Use absolute addresses - RIP-relative won't work from 0x90000

    ; DEBUG: Output 'B' to show we're in long mode
    mov dx, 0x3F8
    mov al, 'B'
    out dx, al

    ; Calculate data base address
    mov rbx, AP_TRAMPOLINE_BASE + (ap_trampoline_data - ap_trampoline_start)

    ; Reload data segments with 64-bit selectors
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov ss, ax
    xor ax, ax
    mov gs, ax  ; Will set GS base via MSR

    ; DEBUG: Output 'C' to show segments reloaded
    mov dx, 0x3F8
    mov al, 'C'
    out dx, al

    ; Load the real 64-bit GDT (pointer from startup data at offset 8)
    mov rax, [rbx + 8]      ; gdt_ptr
    lgdt [rax]

    ; DEBUG: Output 'D' to show real GDT loaded
    mov dx, 0x3F8
    mov al, 'D'
    out dx, al

    ; Reload code segment by far return
    push qword 0x08         ; Code selector
    mov rax, AP_TRAMPOLINE_BASE + (ap_trampoline_64.reload_cs - ap_trampoline_start)
    push rax
    retfq
.reload_cs:

    ; DEBUG: Output 'E' to show CS reloaded
    mov dx, 0x3F8
    mov al, 'E'
    out dx, al

    ; Recalculate data address (registers may have been clobbered)
    mov rbx, AP_TRAMPOLINE_BASE + (ap_trampoline_data - ap_trampoline_start)

    ; Load the real IDT (pointer from startup data at offset 48)
    mov rax, [rbx + 48]     ; idt_ptr
    lidt [rax]

    ; DEBUG: Output 'e' to show IDT loaded
    mov dx, 0x3F8
    mov al, 'e'
    out dx, al

    ; Set up stack from startup data (offset 16)
    mov rsp, [rbx + 16]

    ; DEBUG: Output 'F' to show stack set up
    mov dx, 0x3F8
    mov al, 'F'
    out dx, al

    ; Set GS base to per-CPU state pointer (offset 24)
    mov ecx, 0xC0000101     ; IA32_GS_BASE MSR
    mov rax, [rbx + 24]
    mov rdx, rax
    shr rdx, 32
    wrmsr

    ; DEBUG: Output 'G' to show GS base set
    mov dx, 0x3F8
    mov al, 'G'
    out dx, al

    ; Signal that we're running (offset 40 = ap_running)
    mov dword [rbx + 40], 1
    mfence                      ; Ensure store is visible to other CPUs

    ; DEBUG: Output 'H' to show we signaled running
    mov dx, 0x3F8
    mov al, 'H'
    out dx, al

    ; Call the C# AP entry point
    ; First argument (rcx) = per-CPU state pointer
    mov rcx, [rbx + 24]     ; percpu (offset 24)
    mov rax, [rbx + 32]     ; entry (offset 32)

    ; DEBUG: Output data base address (rbx) and entry address (rax)
    push rax
    push rcx
    push rbx
    mov dx, 0x3F8

    ; Print "[" then rbx (data base)
    mov al, '['
    out dx, al
    mov rcx, rbx            ; Print rbx
    mov r8, 16
.print_rbx:
    rol rcx, 4
    mov al, cl
    and al, 0x0F
    cmp al, 10
    jb .digit_rbx
    add al, 'A' - 10
    jmp .out_rbx
.digit_rbx:
    add al, '0'
.out_rbx:
    out dx, al
    dec r8
    jnz .print_rbx

    ; Print "]@" then rax (entry)
    mov al, ']'
    out dx, al
    mov al, '@'
    out dx, al

    pop rbx
    mov rcx, [rbx + 32]     ; Re-read entry to print
    mov r8, 16
.print_addr:
    rol rcx, 4
    mov al, cl
    and al, 0x0F
    cmp al, 10
    jb .digit
    add al, 'A' - 10
    jmp .output
.digit:
    add al, '0'
.output:
    out dx, al
    dec r8
    jnz .print_addr
    mov al, '>'
    out dx, al
    pop rcx
    pop rax

    ; Align stack to 16-byte boundary before call (required by x64 ABI)
    and rsp, ~0xF
    sub rsp, 0x20           ; Shadow space (32 bytes), keeps 16-byte alignment

    call rax

    ; DEBUG: If we reach here, function returned unexpectedly
    mov dx, 0x3F8
    mov al, 'R'     ; 'R' for Returned
    out dx, al

    ; Should never return, but if it does, halt
.halt_loop:
    cli
    hlt
    jmp .halt_loop

;; Temporary GDT for trampoline - minimal entries for transition
align 16
ap_trampoline_gdt:
    ; Null descriptor (index 0 = 0x00)
    dq 0

    ; 64-bit Code segment (index 1 = 0x08) - for long mode
    ; Base=0, Limit=0xFFFFF, Access=0x9A (present, ring 0, code, exec/read)
    ; Flags=0xA (L=1 for 64-bit, D=0, 4KB granularity)
    dw 0xFFFF       ; Limit low
    dw 0x0000       ; Base low
    db 0x00         ; Base middle
    db 0x9A         ; Access: Present, Ring 0, Code segment, Executable, Readable
    db 0xAF         ; Flags: G=1, L=1, D=0, AVL=0 + Limit high (0xF)
    db 0x00         ; Base high

    ; Data segment (index 2 = 0x10) - 32/64-bit data
    ; Base=0, Limit=0xFFFFF, Access=0x92 (present, ring 0, data, read/write)
    dw 0xFFFF       ; Limit low
    dw 0x0000       ; Base low
    db 0x00         ; Base middle
    db 0x92         ; Access: Present, Ring 0, Data segment, Writable
    db 0xCF         ; Flags: G=1, D/B=1 (32-bit) + Limit high
    db 0x00         ; Base high

    ; 32-bit Code segment (index 3 = 0x18) - for protected mode transition
    ; Base=0, Limit=0xFFFFF, Access=0x9A (present, ring 0, code, exec/read)
    ; Flags=0xC (L=0 for 32-bit, D=1, 4KB granularity)
    dw 0xFFFF       ; Limit low
    dw 0x0000       ; Base low
    db 0x00         ; Base middle
    db 0x9A         ; Access: Present, Ring 0, Code segment, Executable, Readable
    db 0xCF         ; Flags: G=1, D=1 (32-bit), L=0, AVL=0 + Limit high (0xF)
    db 0x00         ; Base high

ap_trampoline_gdt_ptr:
    dw (ap_trampoline_gdt_ptr - ap_trampoline_gdt) - 1  ; Limit (31 bytes = 4 entries - 1)
    dd AP_TRAMPOLINE_BASE + (ap_trampoline_gdt - ap_trampoline_start) ; Base (32-bit absolute)

;; AP startup data structure - embedded in trampoline, filled by BSP
;; Layout must match ApStartupData struct in SMP.cs:
;;   offset 0:  cr3 (8 bytes)
;;   offset 8:  gdt_ptr (8 bytes)
;;   offset 16: stack (8 bytes)
;;   offset 24: percpu (8 bytes)
;;   offset 32: entry (8 bytes)
;;   offset 40: ap_running (4 bytes)
;;   offset 44: ap_id (4 bytes)
;;   offset 48: idt_ptr (8 bytes)
align 8
ap_trampoline_data:
    dq 0            ; offset 0: cr3
    dq 0            ; offset 8: gdt_ptr
    dq 0            ; offset 16: stack
    dq 0            ; offset 24: percpu
    dq 0            ; offset 32: entry
    dd 0            ; offset 40: ap_running
    dd 0            ; offset 44: ap_id
    dq 0            ; offset 48: idt_ptr

ap_trampoline_end:

BITS 64  ; Back to 64-bit for helper functions

;; Helper to get trampoline size
global get_ap_trampoline_size
get_ap_trampoline_size:
    mov rax, ap_trampoline_end - ap_trampoline_start
    ret

;; Helper to get trampoline start address (in kernel memory, before copy)
global get_ap_trampoline_start
get_ap_trampoline_start:
    lea rax, [rel ap_trampoline_start]
    ret

;; Helper to get pointer to ap_startup_data at destination (0x90000)
;; Returns pointer to the data area IN THE COPIED TRAMPOLINE at 0x90000
global get_ap_startup_data
get_ap_startup_data:
    mov rax, AP_TRAMPOLINE_BASE + (ap_trampoline_data - ap_trampoline_start)
    ret
