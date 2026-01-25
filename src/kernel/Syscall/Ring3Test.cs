// ProtonOS kernel - Ring 3 (User Mode) Test
// Tests the transition to user mode and syscall return path.
//
// USAGE:
//   To run this test, add the following call after syscall initialization:
//     Syscall.Ring3Test.Run();
//
//   WARNING: This test will HALT the CPU after completion.
//   Only use for testing the Ring 3 infrastructure.
//
// WHAT IT TESTS:
//   1. User-mode page table permissions (User bit set)
//   2. Jump to Ring 3 via iretq
//   3. User-mode code execution
//   4. SYSCALL instruction from Ring 3
//   5. Syscall handler dispatch
//   6. Return value handling (exit code)
//
// EXPECTED OUTPUT:
//   [Ring3Test] Starting Ring 3 test...
//   [Ring3Test] Code page: 0x... Stack page: 0x...
//   [Ring3Test] Copying N bytes of user code from 0x...
//   [Ring3Test] User RIP: 0x..., User RSP: 0x...
//   [Ring3Test] Jumping to Ring 3...
//   [Syscall] exit(66)
//   [Ring3Test] User mode exited with code: 0x42
//   [Ring3Test] PASS: Ring 3 test succeeded!
//   [Ring3Test] Test complete - halting CPU

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;
using ProtonOS.X64;

namespace ProtonOS.Syscall;

/// <summary>
/// Tests Ring 3 execution and syscall handling.
/// Call Run() to execute the test. CPU will halt after completion.
/// </summary>
public static unsafe class Ring3Test
{
    // Test state
    private static bool _testCompleted;
    private static int _exitCode;
    private static ulong _userCodePage;
    private static ulong _userStackPage;

    /// <summary>
    /// Whether the Ring 3 test completed successfully
    /// </summary>
    public static bool TestCompleted => _testCompleted;

    /// <summary>
    /// Exit code from user mode (should be 0x42 if successful)
    /// </summary>
    public static int ExitCode => _exitCode;

    // Assembly functions
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_user_mode_simple_test_addr();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong get_user_mode_simple_test_size();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void jump_to_ring3(ulong userRip, ulong userRsp);

    /// <summary>
    /// Run the Ring 3 test
    /// </summary>
    /// <returns>True if test passed (exit code was 0x42)</returns>
    public static bool Run()
    {
        DebugConsole.WriteLine("[Ring3Test] Starting Ring 3 test...");

        // Reset state
        _testCompleted = false;
        _exitCode = -1;

        // Check that syscall handling is initialized
        if (!SyscallHandler.IsInitialized)
        {
            DebugConsole.WriteLine("[Ring3Test] ERROR: Syscall handler not initialized!");
            return false;
        }

        // Allocate a page for user code
        _userCodePage = PageAllocator.AllocatePages(1);
        if (_userCodePage == 0)
        {
            DebugConsole.WriteLine("[Ring3Test] ERROR: Failed to allocate code page!");
            return false;
        }

        // Allocate a page for user stack
        _userStackPage = PageAllocator.AllocatePages(1);
        if (_userStackPage == 0)
        {
            DebugConsole.WriteLine("[Ring3Test] ERROR: Failed to allocate stack page!");
            PageAllocator.FreePage(_userCodePage);
            return false;
        }

        DebugConsole.Write("[Ring3Test] Code page: 0x");
        DebugConsole.WriteHex(_userCodePage);
        DebugConsole.Write(", Stack page: 0x");
        DebugConsole.WriteHex(_userStackPage);
        DebugConsole.WriteLine();

        // Make pages user-accessible
        // The page table entries need the User bit (bit 2) set
        if (!MakePagesUserAccessible(_userCodePage, 1))
        {
            DebugConsole.WriteLine("[Ring3Test] ERROR: Failed to make code page user-accessible!");
            Cleanup();
            return false;
        }

        if (!MakePagesUserAccessible(_userStackPage, 1))
        {
            DebugConsole.WriteLine("[Ring3Test] ERROR: Failed to make stack page user-accessible!");
            Cleanup();
            return false;
        }

        // Copy user-mode test code to the code page
        ulong testCodeAddr = get_user_mode_simple_test_addr();
        ulong testCodeSize = get_user_mode_simple_test_size();

        DebugConsole.Write("[Ring3Test] Copying ");
        DebugConsole.WriteDecimal((int)testCodeSize);
        DebugConsole.Write(" bytes of user code from 0x");
        DebugConsole.WriteHex(testCodeAddr);
        DebugConsole.WriteLine();

        // Copy the code
        byte* src = (byte*)testCodeAddr;
        byte* dst = (byte*)_userCodePage;
        for (ulong i = 0; i < testCodeSize; i++)
        {
            dst[i] = src[i];
        }

        // Set up user stack (stack grows down, so start at top of page)
        // Leave some room at the very top for alignment
        ulong userStackTop = _userStackPage + 4096 - 8;
        userStackTop &= ~0xFUL;  // 16-byte align

        DebugConsole.Write("[Ring3Test] User RIP: 0x");
        DebugConsole.WriteHex(_userCodePage);
        DebugConsole.Write(", User RSP: 0x");
        DebugConsole.WriteHex(userStackTop);
        DebugConsole.WriteLine();

        // Register our exit handler so we can capture the exit code
        RegisterExitHandler();

        DebugConsole.WriteLine("[Ring3Test] Jumping to Ring 3...");

        // Jump to Ring 3!
        // This will execute the user code which calls exit(0x42)
        // The exit syscall will be handled and should set _testCompleted
        jump_to_ring3(_userCodePage, userStackTop);

        // We should NOT reach here - exit syscall should handle return
        DebugConsole.WriteLine("[Ring3Test] ERROR: Returned from jump_to_ring3!");
        Cleanup();
        return false;
    }

    /// <summary>
    /// Called by syscall handler when exit() is called from user mode
    /// </summary>
    public static void HandleUserExit(int exitCode)
    {
        _exitCode = exitCode;
        _testCompleted = true;

        DebugConsole.Write("[Ring3Test] User mode exited with code: 0x");
        DebugConsole.WriteHex((uint)exitCode);
        DebugConsole.WriteLine();

        if (exitCode == 0x42)
        {
            DebugConsole.WriteLine("[Ring3Test] PASS: Ring 3 test succeeded!");
            DebugConsole.WriteLine("[Ring3Test] User mode round-trip completed successfully!");
        }
        else
        {
            DebugConsole.WriteLine("[Ring3Test] FAIL: Unexpected exit code!");
        }

        Cleanup();

        // For now, halt the CPU after the test since we can't easily
        // return to the original kernel context. The test has completed.
        // A proper implementation would save/restore a kernel context.
        DebugConsole.WriteLine("[Ring3Test] Test complete - halting CPU");
        CPU.Halt();
    }

    /// <summary>
    /// Make pages user-accessible by setting the User bit in page table entries
    /// </summary>
    private static bool MakePagesUserAccessible(ulong physAddr, int pageCount)
    {
        // We need to set the User bit (bit 2) in the page table entry
        // The physical address should already be mapped in the kernel's page tables
        // We just need to modify the existing mapping to allow user access

        for (int i = 0; i < pageCount; i++)
        {
            ulong addr = physAddr + (ulong)(i * 4096);

            // Since we're using identity mapping in the kernel,
            // physical address = virtual address for low memory
            // We need to find and modify the PTE

            if (!VirtualMemory.SetUserAccessible(addr, true))
            {
                DebugConsole.Write("[Ring3Test] Failed to set User bit for page 0x");
                DebugConsole.WriteHex(addr);
                DebugConsole.WriteLine();
                return false;
            }
        }

        // Flush TLB to ensure changes take effect
        VirtualMemory.FlushTlb();

        return true;
    }

    /// <summary>
    /// Register our exit handler with the syscall dispatcher
    /// </summary>
    private static void RegisterExitHandler()
    {
        // The SyscallDispatch will call our HandleUserExit when exit() is called
        SyscallDispatch.SetRing3TestExitHandler(&HandleUserExitCallback);
    }

    /// <summary>
    /// Callback for exit handler (unmanaged callable)
    /// </summary>
    [UnmanagedCallersOnly]
    private static void HandleUserExitCallback(int exitCode)
    {
        HandleUserExit(exitCode);
    }

    /// <summary>
    /// Clean up allocated resources
    /// </summary>
    private static void Cleanup()
    {
        if (_userCodePage != 0)
        {
            PageAllocator.FreePage(_userCodePage);
            _userCodePage = 0;
        }
        if (_userStackPage != 0)
        {
            PageAllocator.FreePage(_userStackPage);
            _userStackPage = 0;
        }
    }
}
