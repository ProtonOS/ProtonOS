// ProtonOS kernel - Init Process Creation
// Creates and runs the init process (PID 1).

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.X64;
using ProtonOS.IO;
using ProtonOS.Syscall;

namespace ProtonOS.Process;

/// <summary>
/// Init process setup and management
/// </summary>
public static unsafe class InitProcess
{
    // Assembly helper to jump to user mode
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void jump_to_ring3(ulong userRip, ulong userRsp);

    /// <summary>
    /// Create and start the init process with the given code
    /// </summary>
    /// <param name="code">User-mode code to execute</param>
    /// <param name="codeSize">Size of code in bytes</param>
    /// <returns>True if init was started successfully</returns>
    public static bool CreateAndRun(byte* code, ulong codeSize)
    {
        DebugConsole.WriteLine("[Init] Creating init process (PID 1)...");

        // Allocate the init process
        var initProc = ProcessTable.Allocate();
        if (initProc == null)
        {
            DebugConsole.WriteLine("[Init] Failed to allocate init process!");
            return false;
        }

        // Verify it got PID 1
        if (initProc->Pid != 1)
        {
            DebugConsole.Write("[Init] Warning: Init got PID ");
            DebugConsole.WriteDecimal(initProc->Pid);
            DebugConsole.WriteLine(" (expected 1)");
        }

        // Set up init as root
        initProc->Uid = UserIds.Root;
        initProc->Gid = GroupIds.Root;
        initProc->Euid = UserIds.Root;
        initProc->Egid = GroupIds.Root;
        initProc->Suid = UserIds.Root;
        initProc->Sgid = GroupIds.Root;

        // Init is its own session and process group leader
        initProc->SessionId = initProc->Pid;
        initProc->ProcessGroupId = initProc->Pid;

        // Init has no parent (it IS the parent)
        initProc->ParentPid = 0;
        initProc->Parent = null;

        // Set name
        byte* name = stackalloc byte[] { (byte)'i', (byte)'n', (byte)'i', (byte)'t', 0 };
        for (int i = 0; i < 5; i++)
            initProc->Name[i] = name[i];

        // Create user address space
        ulong pml4 = AddressSpace.CreateUserSpace();
        if (pml4 == 0)
        {
            DebugConsole.WriteLine("[Init] Failed to create address space!");
            ProcessTable.Free(initProc);
            return false;
        }
        initProc->PageTableRoot = pml4;

        // Load the code
        ulong entryPoint = BinaryLoader.LoadRawCode(initProc, code, codeSize);
        if (entryPoint == 0)
        {
            DebugConsole.WriteLine("[Init] Failed to load init code!");
            AddressSpace.DestroyUserSpace(pml4);
            ProcessTable.Free(initProc);
            return false;
        }

        // Set up user stack
        ulong userRsp = BinaryLoader.SetupUserStack(initProc, 0);
        if (userRsp == 0)
        {
            DebugConsole.WriteLine("[Init] Failed to set up stack!");
            AddressSpace.DestroyUserSpace(pml4);
            ProcessTable.Free(initProc);
            return false;
        }

        // Set up standard file descriptors (stdin, stdout, stderr)
        SetupStdFds(initProc);

        // Register as init process
        ProcessTable.SetInitProcess(initProc);

        // Create main thread for init
        var initThread = CreateInitThread(initProc, entryPoint, userRsp);
        if (initThread == null)
        {
            DebugConsole.WriteLine("[Init] Failed to create init thread!");
            AddressSpace.DestroyUserSpace(pml4);
            ProcessTable.Free(initProc);
            return false;
        }

        initProc->MainThread = initThread;
        initProc->ThreadCount = 1;
        initProc->State = ProcessState.Running;

        DebugConsole.Write("[Init] Starting, entry=0x");
        DebugConsole.WriteHex(entryPoint);
        DebugConsole.WriteLine();

        // Associate the current boot thread with the init process
        // so that syscalls can find the process context
        var currentThread = Scheduler.CurrentThread;
        if (currentThread != null)
        {
            currentThread->Process = initProc;
            currentThread->IsUserMode = true;
        }

        // Switch to init's address space and jump to user mode
        AddressSpace.SwitchTo(pml4);
        jump_to_ring3(entryPoint, userRsp);

        // Should never return
        DebugConsole.WriteLine("[Init] ERROR: Returned from init!");
        return false;
    }

    /// <summary>
    /// Set up standard file descriptors for init
    /// </summary>
    private static void SetupStdFds(Process* proc)
    {
        // For now, just mark stdin/stdout/stderr as terminal type
        // They'll write to debug console via syscall handler
        if (proc->FdTable != null && proc->FdTableSize >= 3)
        {
            // stdin (fd 0)
            proc->FdTable[0].Type = FileType.Terminal;
            proc->FdTable[0].Flags = FileFlags.ReadOnly;
            proc->FdTable[0].RefCount = 1;

            // stdout (fd 1)
            proc->FdTable[1].Type = FileType.Terminal;
            proc->FdTable[1].Flags = FileFlags.WriteOnly;
            proc->FdTable[1].RefCount = 1;

            // stderr (fd 2)
            proc->FdTable[2].Type = FileType.Terminal;
            proc->FdTable[2].Flags = FileFlags.WriteOnly;
            proc->FdTable[2].RefCount = 1;
        }
    }

    /// <summary>
    /// Create the init thread
    /// </summary>
    private static Thread* CreateInitThread(Process* proc, ulong entryPoint, ulong userRsp)
    {
        // Allocate kernel stack (8KB)
        ulong kernelStack = PageAllocator.AllocatePages(2);
        if (kernelStack == 0)
            return null;

        // Allocate thread structure
        var thread = (Thread*)HeapAllocator.AllocZeroed((ulong)sizeof(Thread));
        if (thread == null)
        {
            PageAllocator.FreePage(kernelStack);
            PageAllocator.FreePage(kernelStack + 4096);
            return null;
        }

        // Set up thread
        thread->Process = proc;
        thread->State = ThreadState.Ready;
        thread->Priority = 128; // Normal priority

        // Kernel stack
        ulong stackTop = kernelStack + 2 * 4096;
        thread->KernelStackTop = stackTop;
        thread->StackBase = stackTop;
        thread->StackLimit = kernelStack;
        thread->StackSize = 2 * 4096;

        // User mode state
        thread->IsUserMode = true;
        thread->UserRip = entryPoint;
        thread->UserRsp = userRsp;

        // Initial register context (will be set up by iretq)
        thread->Context.Rip = entryPoint;
        thread->Context.Rsp = userRsp;
        thread->Context.Rflags = 0x202; // IF=1

        return thread;
    }

    /// <summary>
    /// Create a simple test init that just calls exit(0)
    /// This is useful for testing the process infrastructure without a real init
    /// </summary>
    public static bool CreateTestInit()
    {
        // Simple user-mode code: call exit(0)
        // mov eax, 60     ; syscall: exit
        // xor edi, edi    ; exit code 0
        // syscall
        // ud2             ; should never reach
        byte* code = stackalloc byte[] {
            0xB8, 0x3C, 0x00, 0x00, 0x00,  // mov eax, 60
            0x31, 0xFF,                      // xor edi, edi
            0x0F, 0x05,                      // syscall
            0x0F, 0x0B                       // ud2
        };

        return CreateAndRun(code, 12);
    }

    /// <summary>
    /// Create an init that writes a message and exits
    /// </summary>
    public static bool CreateHelloInit()
    {
        // User-mode code that:
        // 1. Writes "Hello from init!\n" to stdout (fd 1)
        // 2. Calls exit(0)
        //
        // Layout (all offsets from start):
        // 0x00: mov eax, 1          ; 5 bytes - syscall: write
        // 0x05: mov edi, 1          ; 5 bytes - fd: stdout
        // 0x0A: lea rsi, [rip+18]   ; 7 bytes - buf: message (RIP-relative)
        // 0x11: mov edx, 17         ; 5 bytes - count: message length
        // 0x16: syscall             ; 2 bytes
        // 0x18: mov eax, 60         ; 5 bytes - syscall: exit
        // 0x1D: xor edi, edi        ; 2 bytes - code: 0
        // 0x1F: syscall             ; 2 bytes
        // 0x21: ud2                 ; 2 bytes
        // 0x23: "Hello from init!\n" ; 17 bytes
        //
        // LEA displacement: string at 0x23, RIP after LEA = 0x11, so disp = 0x12 = 18

        byte* code = stackalloc byte[] {
            // mov eax, 1 (write syscall)
            0xB8, 0x01, 0x00, 0x00, 0x00,
            // mov edi, 1 (fd = stdout)
            0xBF, 0x01, 0x00, 0x00, 0x00,
            // lea rsi, [rip+18] (buf = message, displacement = 0x12)
            0x48, 0x8D, 0x35, 0x12, 0x00, 0x00, 0x00,
            // mov edx, 17 (count = message length)
            0xBA, 0x11, 0x00, 0x00, 0x00,
            // syscall
            0x0F, 0x05,
            // mov eax, 60 (exit syscall)
            0xB8, 0x3C, 0x00, 0x00, 0x00,
            // xor edi, edi (exit code 0)
            0x31, 0xFF,
            // syscall
            0x0F, 0x05,
            // ud2
            0x0F, 0x0B,
            // "Hello from init!\n" (17 bytes)
            (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', (byte)' ',
            (byte)'f', (byte)'r', (byte)'o', (byte)'m', (byte)' ',
            (byte)'i', (byte)'n', (byte)'i', (byte)'t', (byte)'!', (byte)'\n'
        };

        return CreateAndRun(code, 52);  // 35 bytes code + 17 bytes string
    }
}
