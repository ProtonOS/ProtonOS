// JITTest - Stack Manipulation Tests
// Section 4: dup (0x25), pop (0x26)

namespace JITTest;

/// <summary>
/// Tests for stack manipulation instructions
/// </summary>
public static class StackManipulationTests
{
    public static void RunAll()
    {
        TestDup();
        TestPop();
    }

    #region dup (0x25)

    private static void TestDup()
    {
        // dup duplicates the top value on the stack
        TestTracker.Record("dup.Int32", TestDupInt32() == 42);
        TestTracker.Record("dup.Int64", TestDupInt64() == 0x123456789ABCDEF0L);
        TestTracker.Record("dup.Float", Assert.AreApproxEqual(3.14f, TestDupFloat()));
        TestTracker.Record("dup.Double", Assert.AreApproxEqual(3.14159, TestDupDouble(), 1e-5));
        TestTracker.Record("dup.Object", TestDupObject());
        TestTracker.Record("dup.Null", TestDupNull());
        TestTracker.Record("dup.UsesBothCopies", TestDupUsesBoth() == 84);
    }

    private static int TestDupInt32()
    {
        int x = 42;
        int y = x; // dup + stloc pattern
        return y;
    }

    private static long TestDupInt64()
    {
        long x = 0x123456789ABCDEF0L;
        long y = x;
        return y;
    }

    private static float TestDupFloat()
    {
        float x = 3.14f;
        float y = x;
        return y;
    }

    private static double TestDupDouble()
    {
        double x = 3.14159;
        double y = x;
        return y;
    }

    private static bool TestDupObject()
    {
        object obj = "test";
        object copy = obj;
        return ReferenceEquals(obj, copy);
    }

    private static bool TestDupNull()
    {
        object? obj = null;
        object? copy = obj;
        return copy == null;
    }

    private static int TestDupUsesBoth()
    {
        int x = 42;
        // This pattern uses dup: x + x where x is on stack
        return x + x;
    }

    #endregion

    #region pop (0x26)

    private static void TestPop()
    {
        // pop removes the top value from the stack
        TestTracker.Record("pop.Int32", TestPopInt32() == 42);
        TestTracker.Record("pop.DiscardReturnValue", TestPopDiscardReturn() == 100);
        TestTracker.Record("pop.DiscardObject", TestPopDiscardObject());
    }

    private static int TestPopInt32()
    {
        int x = 10;
        int y = 42;
        _ = x; // pop (discard x)
        return y;
    }

    private static int TestPopDiscardReturn()
    {
        // Method call return value discarded with pop
        GetValue(); // return value popped
        return 100;
    }

    private static int GetValue() => 999;

    private static bool TestPopDiscardObject()
    {
        object obj = "discarded";
        _ = obj; // pop
        return true;
    }

    #endregion
}
