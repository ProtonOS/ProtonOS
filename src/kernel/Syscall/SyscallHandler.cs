// ProtonOS kernel - SYSCALL/SYSRET Infrastructure
// Sets up MSRs for fast system call entry and handles the SYSCALL instruction.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.X64;
using ProtonOS.Process;

namespace ProtonOS.Syscall;

/// <summary>
/// MSR addresses for SYSCALL/SYSRET configuration
/// </summary>
public static class SyscallMsr
{
    /// <summary>
    /// IA32_STAR - Segment selectors for SYSCALL/SYSRET
    /// Bits 47:32 = SYSRET CS/SS base (user code selector - 16)
    /// Bits 63:48 = SYSCALL CS/SS base (kernel code selector)
    /// </summary>
    public const uint IA32_STAR = 0xC0000081;

    /// <summary>
    /// IA32_LSTAR - Long mode SYSCALL target RIP
    /// </summary>
    public const uint IA32_LSTAR = 0xC0000082;

    /// <summary>
    /// IA32_CSTAR - Compatibility mode SYSCALL target (not used in pure 64-bit)
    /// </summary>
    public const uint IA32_CSTAR = 0xC0000083;

    /// <summary>
    /// IA32_FMASK - Flags mask for SYSCALL (bits to clear in RFLAGS)
    /// </summary>
    public const uint IA32_FMASK = 0xC0000084;

    /// <summary>
    /// IA32_EFER - Extended Feature Enable Register
    /// </summary>
    public const uint IA32_EFER = 0xC0000080;

    /// <summary>
    /// EFER.SCE bit - System Call Extensions (enables SYSCALL/SYSRET)
    /// </summary>
    public const ulong EFER_SCE = 1UL << 0;
}

/// <summary>
/// SYSCALL/SYSRET initialization and handling
/// </summary>
public static unsafe class SyscallHandler
{
    private static bool _initialized;

    /// <summary>
    /// Whether syscall handling is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initialize SYSCALL/SYSRET mechanism
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[Syscall] Initializing SYSCALL/SYSRET...");

        // Enable SCE (System Call Extensions) in EFER
        ulong efer = CPU.ReadMsr(SyscallMsr.IA32_EFER);
        efer |= SyscallMsr.EFER_SCE;
        CPU.WriteMsr(SyscallMsr.IA32_EFER, efer);

        // Configure STAR register:
        // STAR[47:32] = SYSRET CS/SS base = 0x10 (so SYSRET loads CS=0x10+16=0x20, SS=0x10+8=0x18)
        // STAR[63:48] = SYSCALL CS/SS base = 0x08 (so SYSCALL loads CS=0x08, SS=0x08+8=0x10)
        //
        // GDT layout:
        //   0x08: Kernel Code (Ring 0)
        //   0x10: Kernel Data (Ring 0)
        //   0x18: User Data (Ring 3)
        //   0x20: User Code (Ring 3)
        //
        // SYSCALL: CS = STAR[63:48] = 0x08 (KernelCode), SS = STAR[63:48]+8 = 0x10 (KernelData)
        // SYSRET:  CS = STAR[47:32]+16 = 0x20 (UserCode), SS = STAR[47:32]+8 = 0x18 (UserData)
        ulong star = ((ulong)0x0008 << 32) | ((ulong)0x0010 << 48);
        CPU.WriteMsr(SyscallMsr.IA32_STAR, star);

        // Get syscall entry point address from assembly
        ulong syscallEntry = GetSyscallEntryAddress();
        CPU.WriteMsr(SyscallMsr.IA32_LSTAR, syscallEntry);

        DebugConsole.Write("[Syscall] Entry point at 0x");
        DebugConsole.WriteHex(syscallEntry);
        DebugConsole.WriteLine();

        // Configure FMASK - clear IF (bit 9) and TF (bit 8) on syscall entry
        // This disables interrupts during the critical syscall prologue
        ulong fmask = (1UL << 9) | (1UL << 8);  // IF | TF
        CPU.WriteMsr(SyscallMsr.IA32_FMASK, fmask);

        // Set up kernel stack for syscall handling
        SetupKernelStack();

        _initialized = true;
        DebugConsole.WriteLine("[Syscall] Initialized");
    }

    /// <summary>
    /// Get the syscall entry point address (from assembly)
    /// </summary>
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_syscall_entry();

    /// <summary>
    /// Set the kernel stack pointer for syscall handling
    /// </summary>
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void set_syscall_kernel_stack(ulong stackTop);

    /// <summary>
    /// Get syscall entry address - wraps the native function
    /// </summary>
    private static ulong GetSyscallEntryAddress()
    {
        return get_syscall_entry();
    }

    /// <summary>
    /// Allocate and set up the syscall kernel stack
    /// </summary>
    private static void SetupKernelStack()
    {
        // Allocate 16KB kernel stack for syscalls (4 pages)
        const int StackPages = 4;
        ulong stackBase = PageAllocator.AllocatePages(StackPages);
        if (stackBase == 0)
        {
            DebugConsole.WriteLine("[Syscall] Failed to allocate kernel stack!");
            return;
        }

        // Stack grows down, so stack top is at high address
        ulong stackTop = stackBase + (StackPages * 4096);

        // Align to 16 bytes (required by x64 ABI)
        stackTop &= ~0xFUL;

        // Set the kernel stack for SYSCALL entry (in assembly)
        set_syscall_kernel_stack(stackTop);

        // Also set TSS RSP0 - used when interrupts occur in Ring 3
        // The CPU automatically loads RSP from TSS.RSP0 when transitioning
        // from Ring 3 to Ring 0 via interrupt/exception
        GDT.SetKernelStack(stackTop);

        DebugConsole.Write("[Syscall] Kernel stack at 0x");
        DebugConsole.WriteHex(stackBase);
        DebugConsole.Write(" - 0x");
        DebugConsole.WriteHex(stackTop);
        DebugConsole.Write(" (TSS.RSP0 set)");
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Main syscall dispatch function - called from assembly syscall entry
    /// </summary>
    /// <param name="number">Syscall number (from RAX)</param>
    /// <param name="arg0">First argument (from RDI)</param>
    /// <param name="arg1">Second argument (from RSI)</param>
    /// <param name="arg2">Third argument (from RDX)</param>
    /// <param name="arg3">Fourth argument (from R10, Linux uses R10 instead of RCX)</param>
    /// <param name="arg4">Fifth argument (from R8)</param>
    /// <param name="arg5">Sixth argument (from R9)</param>
    /// <param name="userRip">User-mode return address (from RCX at syscall entry)</param>
    /// <returns>Syscall result (placed in RAX on return)</returns>
    [UnmanagedCallersOnly(EntryPoint = "SyscallDispatch")]
    public static long Dispatch(long number, long arg0, long arg1, long arg2, long arg3, long arg4, long arg5, long userRip)
    {
        // Get current thread and process
        var thread = Scheduler.CurrentThread;
        var proc = thread != null ? thread->Process : null;

        // Store user RIP in thread for use by clone and other syscalls that need
        // to know where to return to in user mode
        if (thread != null)
        {
            thread->UserRip = (ulong)userRip;
        }

        // Special case: exit syscall (60) can be called without a full process context
        // This is needed for Ring 3 testing where we don't have a full process set up
        if (number == SyscallNumbers.SYS_EXIT)
        {
            int exitCode = (int)arg0;
            DebugConsole.Write("[Syscall] exit(");
            DebugConsole.WriteDecimal(exitCode);
            DebugConsole.WriteLine(")");

            // Check if Ring 3 test handler is installed
            var testHandler = SyscallDispatch.GetRing3TestExitHandler();
            if (testHandler != null)
            {
                DebugConsole.WriteLine("[Syscall] Calling Ring 3 test exit handler");
                SyscallDispatch.ClearRing3TestExitHandler();
                testHandler(exitCode);
                // Handler should not return, but if it does, halt
                CPU.Halt();
            }

            // Normal exit - need a process
            if (proc != null)
            {
                ProcessTable.MarkZombie(proc, exitCode, 0);
            }
            Scheduler.ExitThread((uint)exitCode);
            return 0;
        }

        // For all other syscalls, require a valid process
        if (thread == null || proc == null)
        {
            DebugConsole.Write("[Syscall] ERROR: No process context for syscall ");
            DebugConsole.WriteDecimal((int)number);
            DebugConsole.WriteLine();
            return -IO.Errno.ENOSYS;
        }

        // Dispatch based on syscall number
        return SyscallDispatch.Dispatch(number, arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }
}

/// <summary>
/// Per-CPU syscall state (stored at GS-relative offsets for fast access)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SyscallPerCpu
{
    /// <summary>
    /// Offset 0: Kernel stack pointer for this CPU
    /// </summary>
    public ulong KernelStack;

    /// <summary>
    /// Offset 8: Saved user stack pointer during syscall
    /// </summary>
    public ulong UserStack;

    /// <summary>
    /// Offset 16: Current thread pointer
    /// </summary>
    public Thread* CurrentThread;

    /// <summary>
    /// Offset 24: Current process pointer
    /// </summary>
    public Process.Process* CurrentProcess;
}
