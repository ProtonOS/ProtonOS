// JITTest - Control Flow Tests
// Sections 5-6: Unconditional (jmp, call, calli, ret, br.s, br) and Conditional branches

namespace JITTest;

/// <summary>
/// Tests for control flow instructions
/// </summary>
public static class ControlFlowTests
{
    public static void RunAll()
    {
        // Unconditional branches
        TestBr();
        TestBrS();
        TestRet();
        TestCall();

        // Conditional branches - boolean
        TestBrfalse();
        TestBrtrue();

        // Conditional branches - comparison
        TestBeq();
        TestBne();
        TestBlt();
        TestBle();
        TestBgt();
        TestBge();

        // Unsigned/unordered variants
        TestBltUn();
        TestBleUn();
        TestBgtUn();
        TestBgeUn();
    }

    #region Unconditional Branches

    private static void TestBr()
    {
        TestTracker.Record("br.Forward", TestBrForward() == 42);
        TestTracker.Record("br.Loop", TestBrLoop() == 10);
    }

    private static int TestBrForward()
    {
        goto end;
        #pragma warning disable CS0162
        return 0;
        #pragma warning restore CS0162
        end:
        return 42;
    }

    private static int TestBrLoop()
    {
        int count = 0;
        start:
        count++;
        if (count < 10)
            goto start;
        return count;
    }

    private static void TestBrS()
    {
        TestTracker.Record("br.s.Forward", TestBrSForward() == 42);
    }

    private static int TestBrSForward()
    {
        // br.s is short form for offsets -128 to 127
        goto end;
        #pragma warning disable CS0162
        return 0;
        #pragma warning restore CS0162
        end:
        return 42;
    }

    private static void TestRet()
    {
        TestTracker.Record("ret.Void", TestRetVoid());
        TestTracker.Record("ret.Int32", TestRetInt32() == 42);
        TestTracker.Record("ret.Int64", TestRetInt64() == 0x123456789ABCDEF0L);
        TestTracker.Record("ret.Object", TestRetObject() == "test");
    }

    private static bool TestRetVoid()
    {
        ReturnVoid();
        return true;
    }

    private static void ReturnVoid() { }
    private static int TestRetInt32() => 42;
    private static long TestRetInt64() => 0x123456789ABCDEF0L;
    private static string TestRetObject() => "test";

    private static void TestCall()
    {
        TestTracker.Record("call.Static", CallStaticMethod() == 42);
        TestTracker.Record("call.WithArgs", CallWithArgs(10, 20) == 30);
        TestTracker.Record("call.Recursive", Factorial(5) == 120);
    }

    private static int CallStaticMethod() => StaticHelper();
    private static int StaticHelper() => 42;
    private static int CallWithArgs(int a, int b) => a + b;
    private static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);

    #endregion

    #region Conditional Boolean Branches

    private static void TestBrfalse()
    {
        TestTracker.Record("brfalse.False", TestBrfalseFalse() == 42);
        TestTracker.Record("brfalse.True", TestBrfalseTrue() == 100);
        TestTracker.Record("brfalse.Zero", TestBrfalseZero() == 42);
        TestTracker.Record("brfalse.NonZero", TestBrfalseNonZero() == 100);
        TestTracker.Record("brfalse.Null", TestBrfalseNull() == 42);
    }

    private static int TestBrfalseFalse()
    {
        if (!false) return 42;
        return 0;
    }

    private static int TestBrfalseTrue()
    {
        if (!true) return 0;
        return 100;
    }

    private static int TestBrfalseZero()
    {
        int x = 0;
        if (x == 0) return 42;
        return 0;
    }

    private static int TestBrfalseNonZero()
    {
        int x = 1;
        if (x == 0) return 0;
        return 100;
    }

    private static int TestBrfalseNull()
    {
        object? obj = null;
        if (obj == null) return 42;
        return 0;
    }

    private static void TestBrtrue()
    {
        TestTracker.Record("brtrue.True", TestBrtrueTrue() == 42);
        TestTracker.Record("brtrue.False", TestBrtrueFalse() == 100);
        TestTracker.Record("brtrue.NonZero", TestBrtrueNonZero() == 42);
    }

    private static int TestBrtrueTrue()
    {
        if (true) return 42;
        return 0;
    }

    private static int TestBrtrueFalse()
    {
        if (false) return 0;
        return 100;
    }

    private static int TestBrtrueNonZero()
    {
        int x = 1;
        if (x != 0) return 42;
        return 0;
    }

    #endregion

    #region Conditional Comparison Branches

    private static void TestBeq()
    {
        TestTracker.Record("beq.Equal", TestBeqEqual() == 42);
        TestTracker.Record("beq.NotEqual", TestBeqNotEqual() == 100);
    }

    private static int TestBeqEqual()
    {
        int a = 5, b = 5;
        if (a == b) return 42;
        return 0;
    }

    private static int TestBeqNotEqual()
    {
        int a = 5, b = 10;
        if (a == b) return 0;
        return 100;
    }

    private static void TestBne()
    {
        TestTracker.Record("bne.NotEqual", TestBneNotEqual() == 42);
        TestTracker.Record("bne.Equal", TestBneEqual() == 100);
    }

    private static int TestBneNotEqual()
    {
        int a = 5, b = 10;
        if (a != b) return 42;
        return 0;
    }

    private static int TestBneEqual()
    {
        int a = 5, b = 5;
        if (a != b) return 0;
        return 100;
    }

    private static void TestBlt()
    {
        TestTracker.Record("blt.Less", TestBltLess() == 42);
        TestTracker.Record("blt.NotLess", TestBltNotLess() == 100);
        TestTracker.Record("blt.Equal", TestBltEqual() == 100);
        TestTracker.Record("blt.Negative", TestBltNegative() == 42);
    }

    private static int TestBltLess() { int a = 5, b = 10; if (a < b) return 42; return 0; }
    private static int TestBltNotLess() { int a = 10, b = 5; if (a < b) return 0; return 100; }
    private static int TestBltEqual() { int a = 5, b = 5; if (a < b) return 0; return 100; }
    private static int TestBltNegative() { int a = -10, b = 5; if (a < b) return 42; return 0; }

    private static void TestBle()
    {
        TestTracker.Record("ble.Less", TestBleLess() == 42);
        TestTracker.Record("ble.Equal", TestBleEqual() == 42);
        TestTracker.Record("ble.Greater", TestBleGreater() == 100);
    }

    private static int TestBleLess() { int a = 5, b = 10; if (a <= b) return 42; return 0; }
    private static int TestBleEqual() { int a = 5, b = 5; if (a <= b) return 42; return 0; }
    private static int TestBleGreater() { int a = 10, b = 5; if (a <= b) return 0; return 100; }

    private static void TestBgt()
    {
        TestTracker.Record("bgt.Greater", TestBgtGreater() == 42);
        TestTracker.Record("bgt.NotGreater", TestBgtNotGreater() == 100);
        TestTracker.Record("bgt.Equal", TestBgtEqual() == 100);
    }

    private static int TestBgtGreater() { int a = 10, b = 5; if (a > b) return 42; return 0; }
    private static int TestBgtNotGreater() { int a = 5, b = 10; if (a > b) return 0; return 100; }
    private static int TestBgtEqual() { int a = 5, b = 5; if (a > b) return 0; return 100; }

    private static void TestBge()
    {
        TestTracker.Record("bge.Greater", TestBgeGreater() == 42);
        TestTracker.Record("bge.Equal", TestBgeEqual() == 42);
        TestTracker.Record("bge.Less", TestBgeLess() == 100);
    }

    private static int TestBgeGreater() { int a = 10, b = 5; if (a >= b) return 42; return 0; }
    private static int TestBgeEqual() { int a = 5, b = 5; if (a >= b) return 42; return 0; }
    private static int TestBgeLess() { int a = 5, b = 10; if (a >= b) return 0; return 100; }

    #endregion

    #region Unsigned Comparison Branches

    private static void TestBltUn()
    {
        TestTracker.Record("blt.un.Unsigned", TestBltUnUnsigned() == 42);
        TestTracker.Record("blt.un.HighBit", TestBltUnHighBit() == 42);
    }

    private static int TestBltUnUnsigned()
    {
        uint a = 5, b = 10;
        if (a < b) return 42;
        return 0;
    }

    private static int TestBltUnHighBit()
    {
        // In unsigned comparison, 0x80000000 > 0x7FFFFFFF
        uint a = 0x7FFFFFFF, b = 0x80000000;
        if (a < b) return 42;
        return 0;
    }

    private static void TestBleUn()
    {
        TestTracker.Record("ble.un.LessOrEqual", TestBleUnLessOrEqual() == 42);
    }

    private static int TestBleUnLessOrEqual()
    {
        uint a = 5, b = 5;
        if (a <= b) return 42;
        return 0;
    }

    private static void TestBgtUn()
    {
        TestTracker.Record("bgt.un.Greater", TestBgtUnGreater() == 42);
        TestTracker.Record("bgt.un.HighBit", TestBgtUnHighBit() == 42);
    }

    private static int TestBgtUnGreater()
    {
        uint a = 10, b = 5;
        if (a > b) return 42;
        return 0;
    }

    private static int TestBgtUnHighBit()
    {
        uint a = 0x80000000, b = 0x7FFFFFFF;
        if (a > b) return 42;
        return 0;
    }

    private static void TestBgeUn()
    {
        TestTracker.Record("bge.un.GreaterOrEqual", TestBgeUnGreaterOrEqual() == 42);
    }

    private static int TestBgeUnGreaterOrEqual()
    {
        uint a = 10, b = 10;
        if (a >= b) return 42;
        return 0;
    }

    #endregion
}
