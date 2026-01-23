// JITTest - JIT Regression Tests
// Tests for known JIT issues that were found and fixed

namespace JITTest;

// Helper class for testing class constructors with many args
public class ManyArgClass
{
    public int A, B, C, D, E, F;

    public ManyArgClass(int a, int b, int c, int d, int e, int f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }
}

// Helper class for testing assignment-null-check pattern
public class SimpleIterator
{
    private int _current;
    private int _max;

    public SimpleIterator(int max)
    {
        _current = 0;
        _max = max;
    }

    public string? GetNext()
    {
        if (_current >= _max)
            return null;
        _current++;
        return "item" + _current.ToString();
    }
}

// Helper struct for testing struct constructors with many args
public struct MultiFieldStruct
{
    public int A, B, C, D, E;

    public MultiFieldStruct(int a, int b, int c, int d, int e)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
    }
}

/// <summary>
/// JIT regression tests - verifies fixes for known JIT issues.
/// </summary>
public static class JitRegressionTests
{
    public static void RunAll()
    {
        TestStringReplaceChar();
        TestAssignmentInWhileCondition();
        TestClassCtorManyArgs();
        TestStructCtorManyArgs();
    }

    private static void TestStringReplaceChar()
    {
        // Issue: String.Replace(char, char) crashed in FatFileSystem.NormalizePath
        string input = "a\\b\\c";
        string result = input.Replace('\\', '/');
        TestTracker.Record("jitregr.StringReplaceChar", result == "a/b/c");
    }

    private static void TestAssignmentInWhileCondition()
    {
        // Issue: while ((x = func()) != null) loop exited immediately
        var iter = new SimpleIterator(3);
        int count = 0;
        string? item;
        while ((item = iter.GetNext()) != null)
        {
            count++;
        }
        TestTracker.Record("jitregr.AssignmentInWhile", count == 3);
    }

    private static void TestClassCtorManyArgs()
    {
        // Issue: Class constructor with >4 args failed
        var obj = new ManyArgClass(1, 2, 3, 4, 5, 6);
        int sum = obj.A + obj.B + obj.C + obj.D + obj.E + obj.F;
        TestTracker.Record("jitregr.ClassCtorManyArgs", sum == 21);
    }

    private static void TestStructCtorManyArgs()
    {
        // Baseline: Struct constructor with 5 args should work
        var s = new MultiFieldStruct(1, 2, 3, 4, 5);
        int sum = s.A + s.B + s.C + s.D + s.E;
        TestTracker.Record("jitregr.StructCtorManyArgs", sum == 15);
    }
}
