// ProtonOS kernel - User Mode Syscall Tests
// Generates machine code that tests all implemented syscalls in Ring 3.

using System;
using ProtonOS.Platform;
using ProtonOS.Memory;

namespace ProtonOS.Process;

/// <summary>
/// Generates user-mode test programs that comprehensively test syscalls
/// </summary>
public static unsafe class UserModeTests
{
    /// <summary>
    /// Run comprehensive syscall tests in Ring 3
    /// </summary>
    public static bool RunSyscallTests()
    {
        DebugConsole.WriteLine("[UserModeTests] Running syscall tests in Ring 3...");

        // Build test code dynamically
        var builder = new TestCodeBuilder();

        // Test 1: write syscall (if this works, we can see output)
        builder.EmitTestHeader("write");
        builder.EmitWriteTest();

        // Test 2: mmap anonymous memory
        builder.EmitTestHeader("mmap");
        builder.EmitMmapTest();

        // Test 3: mprotect
        builder.EmitTestHeader("mprotect");
        builder.EmitMprotectTest();

        // Test 4: munmap
        builder.EmitTestHeader("munmap");
        builder.EmitMunmapTest();

        // Test 5: brk
        builder.EmitTestHeader("brk");
        builder.EmitBrkTest();

        // Test 6: lseek (basic test)
        builder.EmitTestHeader("lseek");
        builder.EmitLseekTest();

        // Test 7: getpid
        builder.EmitTestHeader("getpid");
        builder.EmitGetpidTest();

        // Test 8: getuid/geteuid
        builder.EmitTestHeader("getuid");
        builder.EmitGetuidTest();

        // Test 9: getgid/getegid
        builder.EmitTestHeader("getgid");
        builder.EmitGetgidTest();

        // Test 10: close (with invalid fd)
        builder.EmitTestHeader("close");
        builder.EmitCloseTest();

        // Test 11: dup/dup2
        builder.EmitTestHeader("dup/dup2");
        builder.EmitDupTest();

        // Test 12: fstat
        builder.EmitTestHeader("fstat");
        builder.EmitFstatTest();

        // Test 13: pipe
        builder.EmitTestHeader("pipe");
        builder.EmitPipeTest();

        // Test 14: clock_gettime
        builder.EmitTestHeader("clock_gettime");
        builder.EmitClockGettimeTest();

        // Test 15: uname
        builder.EmitTestHeader("uname");
        builder.EmitUnameTest();

        // Test 16: nanosleep
        builder.EmitTestHeader("nanosleep");
        builder.EmitNanosleepTest();

        // Test 17: getrandom
        builder.EmitTestHeader("getrandom");
        builder.EmitGetrandomTest();

        // Summary and exit
        builder.EmitTestSummary();

        byte* code = builder.GetCode();
        int size = builder.GetSize();

        DebugConsole.Write("[UserModeTests] Generated ");
        DebugConsole.WriteDecimal(size);
        DebugConsole.WriteLine(" bytes of test code");

        return InitProcess.CreateAndRun(code, (ulong)size);
    }

    /// <summary>
    /// Helper class for building test machine code
    /// </summary>
    private unsafe struct TestCodeBuilder
    {
        private fixed byte _code[16384];
        private int _offset;
        private int _testCount;
        private int _failCount;

        public byte* GetCode()
        {
            fixed (byte* p = _code)
                return p;
        }

        public int GetSize() => _offset;

        public void EmitTestHeader(string testName)
        {
            _testCount++;
            // Print "Test N: {testName}...\n"
            EmitPrintString("Test ");
            if (_testCount >= 10)
                EmitPrintChar((byte)('0' + _testCount / 10));
            EmitPrintChar((byte)('0' + _testCount % 10));
            EmitPrintString(": ");
            EmitPrintString(testName);
            EmitPrintString("...\n");
        }

        public void EmitWriteTest()
        {
            // Write test already succeeded if we see output!
            EmitPrintString("  [PASS] write syscall works\n");
        }

        public void EmitMmapTest()
        {
            // mmap(NULL, 4096, PROT_READ|PROT_WRITE, MAP_PRIVATE|MAP_ANONYMOUS, -1, 0)
            fixed (byte* code = _code)
            {
                // mov eax, 9 (SYS_MMAP)
                code[_offset++] = 0xB8; Emit32(9);
                // xor edi, edi (addr = NULL)
                code[_offset++] = 0x31; code[_offset++] = 0xFF;
                // mov esi, 4096
                code[_offset++] = 0xBE; Emit32(4096);
                // mov edx, 3 (PROT_READ | PROT_WRITE)
                code[_offset++] = 0xBA; Emit32(3);
                // mov r10d, 0x22 (MAP_PRIVATE | MAP_ANONYMOUS)
                code[_offset++] = 0x41; code[_offset++] = 0xBA; Emit32(0x22);
                // mov r8d, -1
                code[_offset++] = 0x41; code[_offset++] = 0xB8; Emit32(0xFFFFFFFF);
                // xor r9d, r9d
                code[_offset++] = 0x45; code[_offset++] = 0x31; code[_offset++] = 0xC9;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Save in r12
                // mov r12, rax
                code[_offset++] = 0x49; code[_offset++] = 0x89; code[_offset++] = 0xC4;

                // Test: rax should be positive (valid address)
                // test rax, rax
                code[_offset++] = 0x48; code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // js fail
                code[_offset++] = 0x78;
                int failJump1 = _offset++;

                // Write to mapped memory
                // mov dword [r12], 0xDEADBEEF
                code[_offset++] = 0x41; code[_offset++] = 0xC7; code[_offset++] = 0x04; code[_offset++] = 0x24;
                Emit32(0xDEADBEEF);

                // Read back and verify
                // mov eax, [r12]
                code[_offset++] = 0x41; code[_offset++] = 0x8B; code[_offset++] = 0x04; code[_offset++] = 0x24;
                // cmp eax, 0xDEADBEEF
                code[_offset++] = 0x3D; Emit32(0xDEADBEEF);
                // jne fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // PASS
                EmitPrintString("  [PASS] mmap succeeded\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] mmap failed\n");
                // Clear r12
                code[_offset++] = 0x45; code[_offset++] = 0x31; code[_offset++] = 0xE4;

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitMprotectTest()
        {
            fixed (byte* code = _code)
            {
                // Check if we have valid mmap address in r12
                // test r12, r12
                code[_offset++] = 0x4D; code[_offset++] = 0x85; code[_offset++] = 0xE4;
                // jz skip (no valid address)
                code[_offset++] = 0x74;
                int skipJump = _offset++;

                // mprotect(r12, 4096, PROT_READ) - make read-only
                // mov eax, 10
                code[_offset++] = 0xB8; Emit32(10);
                // mov rdi, r12
                code[_offset++] = 0x4C; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // mov esi, 4096
                code[_offset++] = 0xBE; Emit32(4096);
                // mov edx, 1 (PROT_READ)
                code[_offset++] = 0xBA; Emit32(1);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return (0 = success)
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump = _offset++;

                // Restore write permission
                // mov eax, 10
                code[_offset++] = 0xB8; Emit32(10);
                // mov rdi, r12
                code[_offset++] = 0x4C; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // mov esi, 4096
                code[_offset++] = 0xBE; Emit32(4096);
                // mov edx, 3 (PROT_READ|PROT_WRITE)
                code[_offset++] = 0xBA; Emit32(3);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // PASS
                EmitPrintString("  [PASS] mprotect succeeded\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // skip:
                code[skipJump] = (byte)(_offset - skipJump - 1);
                EmitPrintString("  [SKIP] mprotect (no mmap)\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump2 = _offset++;

                // fail:
                code[failJump] = (byte)(_offset - failJump - 1);
                EmitPrintString("  [FAIL] mprotect failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
                code[endJump2] = (byte)(_offset - endJump2 - 1);
            }
        }

        public void EmitMunmapTest()
        {
            fixed (byte* code = _code)
            {
                // Check if we have valid mmap address in r12
                // test r12, r12
                code[_offset++] = 0x4D; code[_offset++] = 0x85; code[_offset++] = 0xE4;
                // jz skip
                code[_offset++] = 0x74;
                int skipJump = _offset++;

                // munmap(r12, 4096)
                // mov eax, 11
                code[_offset++] = 0xB8; Emit32(11);
                // mov rdi, r12
                code[_offset++] = 0x4C; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // mov esi, 4096
                code[_offset++] = 0xBE; Emit32(4096);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return (0 = success)
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump = _offset++;

                // PASS
                EmitPrintString("  [PASS] munmap succeeded\n");
                // Clear r12
                code[_offset++] = 0x45; code[_offset++] = 0x31; code[_offset++] = 0xE4;
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // skip:
                code[skipJump] = (byte)(_offset - skipJump - 1);
                EmitPrintString("  [SKIP] munmap (no mmap)\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump2 = _offset++;

                // fail:
                code[failJump] = (byte)(_offset - failJump - 1);
                EmitPrintString("  [FAIL] munmap failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
                code[endJump2] = (byte)(_offset - endJump2 - 1);
            }
        }

        public void EmitBrkTest()
        {
            fixed (byte* code = _code)
            {
                // Get current brk
                // mov eax, 12
                code[_offset++] = 0xB8; Emit32(12);
                // xor edi, edi
                code[_offset++] = 0x31; code[_offset++] = 0xFF;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Save in r13
                // mov r13, rax
                code[_offset++] = 0x49; code[_offset++] = 0x89; code[_offset++] = 0xC5;

                // Extend brk by 4096
                // lea rdi, [rax + 4096]
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0xB8;
                Emit32(4096);
                // mov eax, 12
                code[_offset++] = 0xB8; Emit32(12);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check if new brk == r13 + 4096
                // lea rcx, [r13 + 4096]
                code[_offset++] = 0x49; code[_offset++] = 0x8D; code[_offset++] = 0x8D;
                Emit32(4096);
                // cmp rax, rcx
                code[_offset++] = 0x48; code[_offset++] = 0x39; code[_offset++] = 0xC8;
                // jne fail
                code[_offset++] = 0x75;
                int failJump = _offset++;

                // PASS
                EmitPrintString("  [PASS] brk succeeded\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump] = (byte)(_offset - failJump - 1);
                EmitPrintString("  [FAIL] brk failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitLseekTest()
        {
            // lseek on stdin should return ESPIPE (illegal seek)
            fixed (byte* code = _code)
            {
                // lseek(0, 0, SEEK_SET)
                // mov eax, 8
                code[_offset++] = 0xB8; Emit32(8);
                // xor edi, edi (fd = 0 = stdin)
                code[_offset++] = 0x31; code[_offset++] = 0xFF;
                // xor esi, esi (offset = 0)
                code[_offset++] = 0x31; code[_offset++] = 0xF6;
                // xor edx, edx (SEEK_SET = 0)
                code[_offset++] = 0x31; code[_offset++] = 0xD2;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Should return -ESPIPE (-29)
                // cmp rax, -29
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xF8;
                code[_offset++] = (byte)(-29 & 0xFF);  // -29 as signed byte
                // jne fail
                code[_offset++] = 0x75;
                int failJump = _offset++;

                // PASS
                EmitPrintString("  [PASS] lseek returns ESPIPE for pipes\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump] = (byte)(_offset - failJump - 1);
                EmitPrintString("  [FAIL] lseek unexpected result\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitGetpidTest()
        {
            // getpid() should return 1 (init process)
            fixed (byte* code = _code)
            {
                // mov eax, 39 (SYS_GETPID)
                code[_offset++] = 0xB8; Emit32(39);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check if pid == 1
                // cmp eax, 1
                code[_offset++] = 0x83; code[_offset++] = 0xF8; code[_offset++] = 0x01;
                // jne fail
                code[_offset++] = 0x75;
                int failJump = _offset++;

                // PASS
                EmitPrintString("  [PASS] getpid returns 1\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump] = (byte)(_offset - failJump - 1);
                EmitPrintString("  [FAIL] getpid did not return 1\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitGetuidTest()
        {
            // getuid() should return 0 (root)
            // geteuid() should also return 0
            fixed (byte* code = _code)
            {
                // mov eax, 102 (SYS_GETUID)
                code[_offset++] = 0xB8; Emit32(102);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check if uid == 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Test geteuid too
                // mov eax, 107 (SYS_GETEUID)
                code[_offset++] = 0xB8; Emit32(107);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check if euid == 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // PASS
                EmitPrintString("  [PASS] getuid/geteuid return 0\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] uid/euid not 0\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitGetgidTest()
        {
            // getgid() should return 0 (root group)
            fixed (byte* code = _code)
            {
                // mov eax, 104 (SYS_GETGID)
                code[_offset++] = 0xB8; Emit32(104);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check if gid == 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Test getegid too
                // mov eax, 108 (SYS_GETEGID)
                code[_offset++] = 0xB8; Emit32(108);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check if egid == 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // PASS
                EmitPrintString("  [PASS] getgid/getegid return 0\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] gid/egid not 0\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitCloseTest()
        {
            // close(-1) should return -EBADF (-9)
            fixed (byte* code = _code)
            {
                // mov eax, 3 (SYS_CLOSE)
                code[_offset++] = 0xB8; Emit32(3);
                // mov edi, -1
                code[_offset++] = 0xBF; Emit32(-1);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Should return -EBADF (-9)
                // cmp rax, -9
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xF8;
                code[_offset++] = (byte)(-9 & 0xFF);
                // jne fail
                code[_offset++] = 0x75;
                int failJump = _offset++;

                // PASS
                EmitPrintString("  [PASS] close(-1) returns EBADF\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump] = (byte)(_offset - failJump - 1);
                EmitPrintString("  [FAIL] close unexpected result\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitDupTest()
        {
            // dup(1) should return new fd >= 3
            // dup2(newfd, 10) should return 10
            fixed (byte* code = _code)
            {
                // mov eax, 32 (SYS_DUP)
                code[_offset++] = 0xB8; Emit32(32);
                // mov edi, 1 (stdout)
                code[_offset++] = 0xBF; Emit32(1);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Save result in r12
                // mov r12, rax
                code[_offset++] = 0x49; code[_offset++] = 0x89; code[_offset++] = 0xC4;

                // Check if fd >= 3 (first free fd)
                // cmp eax, 3
                code[_offset++] = 0x83; code[_offset++] = 0xF8; code[_offset++] = 0x03;
                // jl fail
                code[_offset++] = 0x7C;
                int failJump1 = _offset++;

                // Test dup2: dup2(r12, 10)
                // mov eax, 33 (SYS_DUP2)
                code[_offset++] = 0xB8; Emit32(33);
                // mov rdi, r12
                code[_offset++] = 0x4C; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // mov esi, 10
                code[_offset++] = 0xBE; Emit32(10);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Should return 10
                // cmp eax, 10
                code[_offset++] = 0x83; code[_offset++] = 0xF8; code[_offset++] = 0x0A;
                // jne fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // Close the fds we created
                // close(r12)
                code[_offset++] = 0xB8; Emit32(3);
                code[_offset++] = 0x4C; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                code[_offset++] = 0x0F; code[_offset++] = 0x05;
                // close(10)
                code[_offset++] = 0xB8; Emit32(3);
                code[_offset++] = 0xBF; Emit32(10);
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // PASS
                EmitPrintString("  [PASS] dup/dup2 work correctly\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] dup/dup2 failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);
            }
        }

        public void EmitFstatTest()
        {
            // fstat(1, &buf) on stdout should succeed and return S_IFCHR in st_mode
            // struct stat is 144 bytes, st_mode is at offset 24
            fixed (byte* code = _code)
            {
                // sub rsp, 160 (allocate stack space for stat buffer, 16-byte aligned)
                code[_offset++] = 0x48; code[_offset++] = 0x81; code[_offset++] = 0xEC;
                Emit32(160);

                // fstat(1, rsp) - SYS_FSTAT = 5
                // mov eax, 5
                code[_offset++] = 0xB8; Emit32(5);
                // mov edi, 1 (fd = stdout)
                code[_offset++] = 0xBF; Emit32(1);
                // mov rsi, rsp (buf = stack buffer)
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE6;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return value is 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Check st_mode has S_IFCHR (0x2000) set
                // st_mode is at offset 24 (after st_dev=8, st_ino=8, st_nlink=8)
                // mov eax, [rsp+24]
                code[_offset++] = 0x8B; code[_offset++] = 0x44; code[_offset++] = 0x24;
                code[_offset++] = 24;
                // and eax, 0xF000 (mask for S_IFMT)
                code[_offset++] = 0x25; Emit32(0xF000);
                // cmp eax, 0x2000 (S_IFCHR)
                code[_offset++] = 0x3D; Emit32(0x2000);
                // jne fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // PASS
                EmitPrintString("  [PASS] fstat on stdout returns S_IFCHR\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] fstat failed or wrong mode\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);

                // add rsp, 160 (restore stack)
                code[_offset++] = 0x48; code[_offset++] = 0x81; code[_offset++] = 0xC4;
                Emit32(160);
            }
        }

        public void EmitPipeTest()
        {
            // Test: pipe(fds), write to write end, read from read end, verify
            // SYS_PIPE = 22, SYS_WRITE = 1, SYS_READ = 0, SYS_CLOSE = 3
            fixed (byte* code = _code)
            {
                // sub rsp, 32 (allocate stack for pipefd[2] and buffer)
                // pipefd at rsp+0 (8 bytes), buffer at rsp+16
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xEC;
                code[_offset++] = 32;

                // pipe(rsp) - SYS_PIPE = 22
                // mov eax, 22
                code[_offset++] = 0xB8; Emit32(22);
                // mov rdi, rsp
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return value is 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Store test byte 'P' at rsp+16
                // mov byte [rsp+16], 'P' (0x50)
                code[_offset++] = 0xC6; code[_offset++] = 0x44; code[_offset++] = 0x24;
                code[_offset++] = 16; code[_offset++] = 0x50;

                // write(pipefd[1], rsp+16, 1)
                // mov eax, 1 (SYS_WRITE)
                code[_offset++] = 0xB8; Emit32(1);
                // mov edi, [rsp+4] (pipefd[1] - write end)
                code[_offset++] = 0x8B; code[_offset++] = 0x7C; code[_offset++] = 0x24;
                code[_offset++] = 4;
                // lea rsi, [rsp+16]
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0x74;
                code[_offset++] = 0x24; code[_offset++] = 16;
                // mov edx, 1
                code[_offset++] = 0xBA; Emit32(1);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check write returned 1
                // cmp eax, 1
                code[_offset++] = 0x83; code[_offset++] = 0xF8; code[_offset++] = 1;
                // jne fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // Clear the buffer location for read
                // mov byte [rsp+16], 0
                code[_offset++] = 0xC6; code[_offset++] = 0x44; code[_offset++] = 0x24;
                code[_offset++] = 16; code[_offset++] = 0;

                // read(pipefd[0], rsp+16, 1)
                // mov eax, 0 (SYS_READ)
                code[_offset++] = 0xB8; Emit32(0);
                // mov edi, [rsp] (pipefd[0] - read end)
                code[_offset++] = 0x8B; code[_offset++] = 0x3C; code[_offset++] = 0x24;
                // lea rsi, [rsp+16]
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0x74;
                code[_offset++] = 0x24; code[_offset++] = 16;
                // mov edx, 1
                code[_offset++] = 0xBA; Emit32(1);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check read returned 1
                // cmp eax, 1
                code[_offset++] = 0x83; code[_offset++] = 0xF8; code[_offset++] = 1;
                // jne fail
                code[_offset++] = 0x75;
                int failJump3 = _offset++;

                // Check we read 'P' back
                // cmp byte [rsp+16], 'P'
                code[_offset++] = 0x80; code[_offset++] = 0x7C; code[_offset++] = 0x24;
                code[_offset++] = 16; code[_offset++] = 0x50;
                // jne fail
                code[_offset++] = 0x75;
                int failJump4 = _offset++;

                // Close both ends
                // close(pipefd[0])
                code[_offset++] = 0xB8; Emit32(3);
                code[_offset++] = 0x8B; code[_offset++] = 0x3C; code[_offset++] = 0x24;
                code[_offset++] = 0x0F; code[_offset++] = 0x05;
                // close(pipefd[1])
                code[_offset++] = 0xB8; Emit32(3);
                code[_offset++] = 0x8B; code[_offset++] = 0x7C; code[_offset++] = 0x24;
                code[_offset++] = 4;
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // PASS
                EmitPrintString("  [PASS] pipe read/write works\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                code[failJump3] = (byte)(_offset - failJump3 - 1);
                code[failJump4] = (byte)(_offset - failJump4 - 1);
                EmitPrintString("  [FAIL] pipe test failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);

                // add rsp, 32 (restore stack)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xC4;
                code[_offset++] = 32;
            }
        }

        public void EmitClockGettimeTest()
        {
            // Test: clock_gettime(CLOCK_MONOTONIC, &ts) should return 0 and fill timespec
            // SYS_CLOCK_GETTIME = 228, CLOCK_MONOTONIC = 1
            // struct timespec { long tv_sec; long tv_nsec; } = 16 bytes
            fixed (byte* code = _code)
            {
                // sub rsp, 32 (allocate stack for timespec, 16-byte aligned)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xEC;
                code[_offset++] = 32;

                // Zero the timespec
                // mov qword [rsp], 0
                code[_offset++] = 0x48; code[_offset++] = 0xC7; code[_offset++] = 0x04;
                code[_offset++] = 0x24; Emit32(0);
                // mov qword [rsp+8], 0
                code[_offset++] = 0x48; code[_offset++] = 0xC7; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 8; Emit32(0);

                // clock_gettime(CLOCK_MONOTONIC, rsp) - SYS_CLOCK_GETTIME = 228
                // mov eax, 228
                code[_offset++] = 0xB8; Emit32(228);
                // mov edi, 1 (CLOCK_MONOTONIC)
                code[_offset++] = 0xBF; Emit32(1);
                // mov rsi, rsp (ts = stack buffer)
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE6;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return value is 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Check tv_sec or tv_nsec is non-zero (time has passed since boot)
                // mov rax, [rsp] (tv_sec)
                code[_offset++] = 0x48; code[_offset++] = 0x8B; code[_offset++] = 0x04;
                code[_offset++] = 0x24;
                // or rax, [rsp+8] (tv_nsec)
                code[_offset++] = 0x48; code[_offset++] = 0x0B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 8;
                // test rax, rax
                code[_offset++] = 0x48; code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jz fail (if both are zero, something's wrong)
                code[_offset++] = 0x74;
                int failJump2 = _offset++;

                // Check tv_nsec is valid (< 1000000000)
                // mov rax, [rsp+8]
                code[_offset++] = 0x48; code[_offset++] = 0x8B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 8;
                // cmp rax, 1000000000
                code[_offset++] = 0x48; code[_offset++] = 0x3D; Emit32(1000000000);
                // jge fail
                code[_offset++] = 0x7D;
                int failJump3 = _offset++;

                // PASS
                EmitPrintString("  [PASS] clock_gettime returns valid time\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                code[failJump3] = (byte)(_offset - failJump3 - 1);
                EmitPrintString("  [FAIL] clock_gettime failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);

                // add rsp, 32 (restore stack)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xC4;
                code[_offset++] = 32;
            }
        }

        public void EmitUnameTest()
        {
            // Test: uname(&buf) should return 0 and fill sysname with "ProtonOS"
            // SYS_UNAME = 63
            // struct utsname is 6 * 65 = 390 bytes, but we'll allocate 400 (16-byte aligned)
            fixed (byte* code = _code)
            {
                // sub rsp, 400 (allocate stack for utsname)
                code[_offset++] = 0x48; code[_offset++] = 0x81; code[_offset++] = 0xEC;
                Emit32(400);

                // uname(rsp) - SYS_UNAME = 63
                // mov eax, 63
                code[_offset++] = 0xB8; Emit32(63);
                // mov rdi, rsp (buf = stack buffer)
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return value is 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Check sysname starts with 'P' (for "ProtonOS")
                // sysname is at offset 0
                // cmp byte [rsp], 'P'
                code[_offset++] = 0x80; code[_offset++] = 0x3C; code[_offset++] = 0x24;
                code[_offset++] = (byte)'P';
                // jne fail
                code[_offset++] = 0x75;
                int failJump2 = _offset++;

                // Check machine starts with 'x' (for "x86_64")
                // machine is at offset 65 * 4 = 260
                // cmp byte [rsp+260], 'x'
                code[_offset++] = 0x80; code[_offset++] = 0xBC; code[_offset++] = 0x24;
                Emit32(260);
                code[_offset++] = (byte)'x';
                // jne fail
                code[_offset++] = 0x75;
                int failJump3 = _offset++;

                // PASS
                EmitPrintString("  [PASS] uname returns ProtonOS/x86_64\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                code[failJump3] = (byte)(_offset - failJump3 - 1);
                EmitPrintString("  [FAIL] uname failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);

                // add rsp, 400 (restore stack)
                code[_offset++] = 0x48; code[_offset++] = 0x81; code[_offset++] = 0xC4;
                Emit32(400);
            }
        }

        public void EmitNanosleepTest()
        {
            // Test: nanosleep for 1ms, verify it returns 0
            // Also verify time elapsed by checking clock_gettime before and after
            // SYS_NANOSLEEP = 35, SYS_CLOCK_GETTIME = 228
            // struct timespec { long tv_sec; long tv_nsec; } = 16 bytes
            fixed (byte* code = _code)
            {
                // sub rsp, 64 (allocate stack: req@0, rem@16, before@32, after@48)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xEC;
                code[_offset++] = 64;

                // Get time before sleep: clock_gettime(CLOCK_MONOTONIC, rsp+32)
                // mov eax, 228
                code[_offset++] = 0xB8; Emit32(228);
                // mov edi, 1 (CLOCK_MONOTONIC)
                code[_offset++] = 0xBF; Emit32(1);
                // lea rsi, [rsp+32]
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0x74;
                code[_offset++] = 0x24; code[_offset++] = 32;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Set up req timespec: 0 seconds, 1000000 nanoseconds (1ms)
                // mov qword [rsp], 0 (tv_sec)
                code[_offset++] = 0x48; code[_offset++] = 0xC7; code[_offset++] = 0x04;
                code[_offset++] = 0x24; Emit32(0);
                // mov dword [rsp+8], 1000000 (tv_nsec = 1ms)
                code[_offset++] = 0xC7; code[_offset++] = 0x44; code[_offset++] = 0x24;
                code[_offset++] = 8; Emit32(1000000);
                // mov dword [rsp+12], 0 (high 32 bits of tv_nsec)
                code[_offset++] = 0xC7; code[_offset++] = 0x44; code[_offset++] = 0x24;
                code[_offset++] = 12; Emit32(0);

                // nanosleep(rsp, rsp+16) - SYS_NANOSLEEP = 35
                // mov eax, 35
                code[_offset++] = 0xB8; Emit32(35);
                // mov rdi, rsp (req)
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // lea rsi, [rsp+16] (rem)
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0x74;
                code[_offset++] = 0x24; code[_offset++] = 16;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return value is 0
                // test eax, eax
                code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jnz fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Get time after sleep: clock_gettime(CLOCK_MONOTONIC, rsp+48)
                // mov eax, 228
                code[_offset++] = 0xB8; Emit32(228);
                // mov edi, 1
                code[_offset++] = 0xBF; Emit32(1);
                // lea rsi, [rsp+48]
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0x74;
                code[_offset++] = 0x24; code[_offset++] = 48;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Compute elapsed nanoseconds (simplified: just check after.tv_nsec >= before.tv_nsec
                // or after.tv_sec > before.tv_sec - this handles the common case)
                // mov rax, [rsp+48] (after.tv_sec)
                code[_offset++] = 0x48; code[_offset++] = 0x8B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 48;
                // cmp rax, [rsp+32] (before.tv_sec)
                code[_offset++] = 0x48; code[_offset++] = 0x3B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 32;
                // jg pass (if after_sec > before_sec, definitely slept)
                code[_offset++] = 0x7F;
                int passJump1 = _offset++;

                // If seconds equal, check nanoseconds increased by at least some amount
                // mov rax, [rsp+56] (after.tv_nsec)
                code[_offset++] = 0x48; code[_offset++] = 0x8B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 56;
                // sub rax, [rsp+40] (before.tv_nsec)
                code[_offset++] = 0x48; code[_offset++] = 0x2B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 40;
                // cmp rax, 500000 (at least 0.5ms elapsed - allow some tolerance)
                code[_offset++] = 0x48; code[_offset++] = 0x3D; Emit32(500000);
                // jl fail
                code[_offset++] = 0x7C;
                int failJump2 = _offset++;

                // pass:
                code[passJump1] = (byte)(_offset - passJump1 - 1);
                EmitPrintString("  [PASS] nanosleep works\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] nanosleep failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);

                // add rsp, 64 (restore stack)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xC4;
                code[_offset++] = 64;
            }
        }

        public void EmitGetrandomTest()
        {
            // Test: getrandom(buf, 16, 0) should return 16 and fill buffer with non-zero data
            // SYS_GETRANDOM = 318
            fixed (byte* code = _code)
            {
                // sub rsp, 32 (allocate 16 bytes for buffer, 16-byte aligned)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xEC;
                code[_offset++] = 32;

                // Zero the buffer first
                // mov qword [rsp], 0
                code[_offset++] = 0x48; code[_offset++] = 0xC7; code[_offset++] = 0x04;
                code[_offset++] = 0x24; Emit32(0);
                // mov qword [rsp+8], 0
                code[_offset++] = 0x48; code[_offset++] = 0xC7; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 8; Emit32(0);

                // getrandom(rsp, 16, 0) - SYS_GETRANDOM = 318
                // mov eax, 318
                code[_offset++] = 0xB8; Emit32(318);
                // mov rdi, rsp (buf)
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE7;
                // mov esi, 16 (buflen)
                code[_offset++] = 0xBE; Emit32(16);
                // xor edx, edx (flags = 0)
                code[_offset++] = 0x31; code[_offset++] = 0xD2;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;

                // Check return value is 16
                // cmp eax, 16
                code[_offset++] = 0x83; code[_offset++] = 0xF8; code[_offset++] = 16;
                // jne fail
                code[_offset++] = 0x75;
                int failJump1 = _offset++;

                // Check that buffer is not all zeros (at least one qword should be non-zero)
                // mov rax, [rsp]
                code[_offset++] = 0x48; code[_offset++] = 0x8B; code[_offset++] = 0x04;
                code[_offset++] = 0x24;
                // or rax, [rsp+8]
                code[_offset++] = 0x48; code[_offset++] = 0x0B; code[_offset++] = 0x44;
                code[_offset++] = 0x24; code[_offset++] = 8;
                // test rax, rax
                code[_offset++] = 0x48; code[_offset++] = 0x85; code[_offset++] = 0xC0;
                // jz fail (if both qwords are zero, something's wrong)
                code[_offset++] = 0x74;
                int failJump2 = _offset++;

                // PASS
                EmitPrintString("  [PASS] getrandom returns random bytes\n");
                // jmp end
                code[_offset++] = 0xEB;
                int endJump = _offset++;

                // fail:
                code[failJump1] = (byte)(_offset - failJump1 - 1);
                code[failJump2] = (byte)(_offset - failJump2 - 1);
                EmitPrintString("  [FAIL] getrandom failed\n");

                // end:
                code[endJump] = (byte)(_offset - endJump - 1);

                // add rsp, 32 (restore stack)
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xC4;
                code[_offset++] = 32;
            }
        }

        public void EmitTestSummary()
        {
            EmitPrintString("\n=== Syscall tests complete ===\n");

            fixed (byte* code = _code)
            {
                // exit(0)
                // mov eax, 60
                code[_offset++] = 0xB8; Emit32(60);
                // xor edi, edi
                code[_offset++] = 0x31; code[_offset++] = 0xFF;
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;
                // ud2 (shouldn't reach here)
                code[_offset++] = 0x0F; code[_offset++] = 0x0B;
            }
        }

        private void EmitPrintString(string s)
        {
            // For each string, emit inline:
            // jmp over_string
            // string_data:
            // .ascii "string"
            // over_string:
            // write(1, string_addr, len)

            fixed (byte* code = _code)
            {
                // jmp over string data
                code[_offset++] = 0xEB;
                int jmpOffset = _offset++;

                // String data
                int stringStart = _offset;
                for (int i = 0; i < s.Length; i++)
                    code[_offset++] = (byte)s[i];
                int stringLen = _offset - stringStart;

                // Patch jump
                code[jmpOffset] = (byte)(_offset - jmpOffset - 1);

                // write(1, string_addr, len)
                // mov eax, 1 (SYS_WRITE)
                code[_offset++] = 0xB8; Emit32(1);
                // mov edi, 1 (stdout)
                code[_offset++] = 0xBF; Emit32(1);
                // lea rsi, [rip - X] where X points back to string
                code[_offset++] = 0x48; code[_offset++] = 0x8D; code[_offset++] = 0x35;
                // Displacement: string_start - (current_offset + 4)
                int displacement = stringStart - (_offset + 4);
                Emit32(displacement);
                // mov edx, len
                code[_offset++] = 0xBA; Emit32(stringLen);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;
            }
        }

        private void EmitPrintChar(byte c)
        {
            fixed (byte* code = _code)
            {
                // Push char onto stack and write from there
                // push c
                code[_offset++] = 0x6A; code[_offset++] = c;
                // write(1, rsp, 1)
                // mov eax, 1
                code[_offset++] = 0xB8; Emit32(1);
                // mov edi, 1
                code[_offset++] = 0xBF; Emit32(1);
                // mov rsi, rsp
                code[_offset++] = 0x48; code[_offset++] = 0x89; code[_offset++] = 0xE6;
                // mov edx, 1
                code[_offset++] = 0xBA; Emit32(1);
                // syscall
                code[_offset++] = 0x0F; code[_offset++] = 0x05;
                // pop (clean stack)
                // add rsp, 8
                code[_offset++] = 0x48; code[_offset++] = 0x83; code[_offset++] = 0xC4; code[_offset++] = 0x08;
            }
        }

        private void Emit32(int value)
        {
            fixed (byte* code = _code)
            {
                code[_offset++] = (byte)(value & 0xFF);
                code[_offset++] = (byte)((value >> 8) & 0xFF);
                code[_offset++] = (byte)((value >> 16) & 0xFF);
                code[_offset++] = (byte)((value >> 24) & 0xFF);
            }
        }

        private void Emit32(uint value)
        {
            Emit32((int)value);
        }
    }
}
