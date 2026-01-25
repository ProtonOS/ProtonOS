// ArgsApp - Test application for execve with arguments
// Tests that Main(string[] args) receives arguments correctly.

namespace ArgsApp;

public static class Program
{
    // Main with string[] args - returns the argument count
    public static int Main(string[] args)
    {
        // Return the number of arguments passed
        // This lets us verify args are being passed correctly
        if (args == null)
            return -1;  // Error: args should never be null

        return args.Length;
    }
}
