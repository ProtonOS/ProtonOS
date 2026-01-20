// JITTest - Entry Point
// Main entry point for IL opcode testing

namespace JITTest;

/// <summary>
/// Program entry point - delegates to TestRunner
/// </summary>
public static class Program
{
    /// <summary>
    /// Main method - runs all IL opcode tests
    /// </summary>
    public static int Main()
    {
        return TestRunner.RunAllTests();
    }
}
