// JITTest - Interlocked Tests
// Tests System.Threading.Interlocked atomic operations

using System.Threading;

namespace JITTest;

/// <summary>
/// Interlocked tests - verifies atomic operations.
/// Tests Increment, Decrement, Exchange, CompareExchange, Add.
/// </summary>
public static class InterlockedTests
{
    public static void RunAll()
    {
        TestIncrement();
        TestDecrement();
        TestExchange();
        TestCompareExchangeSuccess();
        TestCompareExchangeFail();
        TestAdd();
        TestIncrement64();
        TestCompareExchange64();
    }

    private static void TestIncrement()
    {
        int value = 10;
        int result = Interlocked.Increment(ref value);
        TestTracker.Record("interlocked.Increment", result == 11 && value == 11);
    }

    private static void TestDecrement()
    {
        int value = 10;
        int result = Interlocked.Decrement(ref value);
        TestTracker.Record("interlocked.Decrement", result == 9 && value == 9);
    }

    private static void TestExchange()
    {
        int value = 10;
        int original = Interlocked.Exchange(ref value, 20);
        TestTracker.Record("interlocked.Exchange", original == 10 && value == 20);
    }

    private static void TestCompareExchangeSuccess()
    {
        int value = 10;
        int original = Interlocked.CompareExchange(ref value, 20, 10);
        // Exchange should happen: original=10, value=20
        TestTracker.Record("interlocked.CompareExchangeSuccess", original == 10 && value == 20);
    }

    private static void TestCompareExchangeFail()
    {
        int value = 10;
        int original = Interlocked.CompareExchange(ref value, 20, 5);
        // Exchange should NOT happen: original=10, value=10
        TestTracker.Record("interlocked.CompareExchangeFail", original == 10 && value == 10);
    }

    private static void TestAdd()
    {
        int value = 10;
        int result = Interlocked.Add(ref value, 5);
        TestTracker.Record("interlocked.Add", result == 15 && value == 15);
    }

    private static void TestIncrement64()
    {
        long value = 10L;
        long result = Interlocked.Increment(ref value);
        TestTracker.Record("interlocked.Increment64", result == 11L && value == 11L);
    }

    private static void TestCompareExchange64()
    {
        long value = 100L;
        long original = Interlocked.CompareExchange(ref value, 200L, 100L);
        TestTracker.Record("interlocked.CompareExchange64", original == 100L && value == 200L);
    }
}
