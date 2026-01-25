// HelloApp - Simple test application for execve
// This is a minimal .NET application to test the execve syscall.

namespace HelloApp;

public static class Program
{
    // The simplest possible Main - just returns 0
    // The JIT will compile this to basically just "xor eax,eax; ret"
    public static int Main()
    {
        return 42; // Return 42 to prove we ran
    }
}
