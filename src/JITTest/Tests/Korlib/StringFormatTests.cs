// JITTest - String Format Tests
// Tests string.Format operations

namespace JITTest;

/// <summary>
/// String format tests - verifies string.Format operations.
/// Tests formatting with various argument counts and types.
/// </summary>
public static class StringFormatTests
{
    public static void RunAll()
    {
        TestFormatOneArg();
        TestFormatTwoArgs();
        TestFormatThreeArgs();
        TestFormatStringArg();
    }

    private static void TestFormatOneArg()
    {
        int x = 42;
        string s = string.Format("Value: {0}", x);
        // Expected: "Value: 42" which has length 9
        TestTracker.Record("stringformat.OneArg", s.Length == 9);
    }

    private static void TestFormatTwoArgs()
    {
        int a = 10;
        int b = 20;
        string s = string.Format("{0} + {1}", a, b);
        // Expected: "10 + 20" which has length 7
        TestTracker.Record("stringformat.TwoArgs", s.Length == 7);
    }

    private static void TestFormatThreeArgs()
    {
        int a = 10;
        int b = 20;
        int c = 30;
        string s = string.Format("{0} + {1} = {2}", a, b, c);
        // Expected: "10 + 20 = 30" which has length 12
        TestTracker.Record("stringformat.ThreeArgs", s.Length == 12);
    }

    private static void TestFormatStringArg()
    {
        string name = "World";
        string s = string.Format("Hello, {0}!", name);
        // Expected: "Hello, World!" which has length 13
        TestTracker.Record("stringformat.StringArg", s.Length == 13);
    }
}
