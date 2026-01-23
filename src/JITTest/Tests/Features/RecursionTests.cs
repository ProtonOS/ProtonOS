// JITTest - Recursion Tests
// Tests recursive method calls

namespace JITTest;

/// <summary>
/// Recursion tests - verifies recursive method calls work correctly.
/// Tests factorial, fibonacci, sum, and mutual recursion.
/// </summary>
public static class RecursionTests
{
    public static void RunAll()
    {
        TestFactorialBasic();
        TestFactorialAccumulator();
        TestRecursiveSum();
        TestRecursiveFib();
        TestMutualRecursion();
    }

    private static void TestFactorialBasic()
    {
        // 5! = 120
        int result = Factorial(5);
        TestTracker.Record("recursion.FactorialBasic", result == 120);
    }

    private static int Factorial(int n)
    {
        if (n <= 1) return 1;
        return n * Factorial(n - 1);
    }

    private static void TestFactorialAccumulator()
    {
        // Tail-recursive style (accumulator pattern)
        int result = FactorialAcc(5, 1);
        TestTracker.Record("recursion.FactorialAccumulator", result == 120);
    }

    private static int FactorialAcc(int n, int acc)
    {
        if (n <= 1) return acc;
        return FactorialAcc(n - 1, acc * n);
    }

    private static void TestRecursiveSum()
    {
        // 1+2+...+10 = 55
        int result = Sum(10);
        TestTracker.Record("recursion.Sum", result == 55);
    }

    private static int Sum(int n)
    {
        if (n <= 0) return 0;
        return n + Sum(n - 1);
    }

    private static void TestRecursiveFib()
    {
        // Fib(10) = 55
        int result = Fib(10);
        TestTracker.Record("recursion.Fib", result == 55);
    }

    private static int Fib(int n)
    {
        if (n <= 1) return n;
        return Fib(n - 1) + Fib(n - 2);
    }

    private static void TestMutualRecursion()
    {
        // IsEven and IsOdd call each other
        bool even10 = IsEven(10);
        bool odd10 = IsOdd(10);
        bool even7 = IsEven(7);
        bool odd7 = IsOdd(7);
        TestTracker.Record("recursion.MutualRecursion",
            even10 && !odd10 && !even7 && odd7);
    }

    private static bool IsEven(int n)
    {
        if (n == 0) return true;
        return IsOdd(n - 1);
    }

    private static bool IsOdd(int n)
    {
        if (n == 0) return false;
        return IsEven(n - 1);
    }
}
