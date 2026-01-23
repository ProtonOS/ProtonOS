// JITTest - String Tests
// Tests string operations (ldstr, String.Concat, String.Replace, etc.)

namespace JITTest;

/// <summary>
/// String tests - verifies string operations from JIT code.
/// Tests ldstr, String.Concat, String.Replace via AOT method calls.
/// </summary>
public static class StringTests
{
    public static void RunAll()
    {
        TestLdstr();
        TestStringConcat();
        TestStringReplace();
        TestStringLength();
        TestStringContains();
        TestStringIndexOf();
    }

    private static void TestLdstr()
    {
        string s = "hello";
        TestTracker.Record("string.Ldstr", s.Length == 5);
    }

    private static void TestStringConcat()
    {
        string a = "hel";
        string b = "lo";
        string result = a + b;
        TestTracker.Record("string.Concat", result.Length == 5);
    }

    private static void TestStringReplace()
    {
        string s = "hello";
        string result = s.Replace('l', 'x');
        // "hello" -> "hexxo" (length still 5)
        TestTracker.Record("string.Replace", result.Length == 5 && result != s);
    }

    private static void TestStringLength()
    {
        string s = "test string";
        TestTracker.Record("string.Length", s.Length == 11);
    }

    private static void TestStringContains()
    {
        string s = "hello world";
        TestTracker.Record("string.ContainsTrue", s.Contains("world"));
        TestTracker.Record("string.ContainsFalse", !s.Contains("xyz"));
    }

    private static void TestStringIndexOf()
    {
        string s = "hello world";
        TestTracker.Record("string.IndexOfFound", s.IndexOf('w') == 6);
        TestTracker.Record("string.IndexOfNotFound", s.IndexOf('z') == -1);
    }
}
