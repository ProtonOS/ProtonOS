// JITTest - Control Flow Tests
// Sections 5-7: Unconditional (br, br.s, ret, call), Conditional Boolean, Conditional Comparison

namespace JITTest;

/// <summary>
/// Tests for control flow instructions (ECMA-335 Section III)
/// Section 5: Unconditional control flow
/// Section 6: Conditional boolean branches
/// Section 7: Conditional comparison branches
/// </summary>
public static class ControlFlowTests
{
    public static void RunAll()
    {
        // Section 5: Unconditional control flow
        TestBr();
        TestBrS();
        TestRet();
        TestCall();

        // Section 6: Conditional boolean branches
        TestBrfalse();
        TestBrtrue();

        // Section 7: Conditional comparison branches - signed
        TestBeq();
        TestBne();
        TestBlt();
        TestBle();
        TestBgt();
        TestBge();

        // Section 7: Conditional comparison branches - unsigned/unordered
        TestBltUn();
        TestBleUn();
        TestBgtUn();
        TestBgeUn();
        TestBneUn();

        // Additional comparison branch tests with different types
        TestBranchesInt64();
        TestBranchesFloat();

        // Small type branches (byte, sbyte, short, ushort, char)
        RunSmallTypeBranchTests();
    }

    #region Section 5.5-5.6: br.s and br (Unconditional Branch)

    private static void TestBr()
    {
        // Basic forward branch
        TestTracker.Record("br.Forward", TestBrForward() == 42);
        TestTracker.Record("br.SkipCode", TestBrSkipCode() == 100);

        // Backward branch (loops)
        TestTracker.Record("br.Loop", TestBrLoop() == 10);
        TestTracker.Record("br.LoopSum", TestBrLoopSum() == 55);

        // Branch chain
        TestTracker.Record("br.Chain", TestBrChain() == 3);

        // Stack preservation
        TestTracker.Record("br.PreservesStack", TestBrPreservesStack() == 42);
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

    private static int TestBrSkipCode()
    {
        int x = 0;
        goto skip;
        #pragma warning disable CS0162
        x = 999;
        #pragma warning restore CS0162
        skip:
        return x + 100;
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

    private static int TestBrLoopSum()
    {
        int sum = 0;
        int i = 1;
        start:
        sum += i;
        i++;
        if (i <= 10)
            goto start;
        return sum;  // 1+2+3+...+10 = 55
    }

    private static int TestBrChain()
    {
        int step = 0;
        goto step1;
        #pragma warning disable CS0162
        return -1;
        #pragma warning restore CS0162

        step1:
        step++;
        goto step2;

        step2:
        step++;
        goto step3;

        step3:
        step++;
        return step;
    }

    private static int TestBrPreservesStack()
    {
        int value = 42;
        goto skip;
        #pragma warning disable CS0162
        value = 0;
        #pragma warning restore CS0162
        skip:
        return value;
    }

    private static void TestBrS()
    {
        // Short form tests (offset -128 to 127)
        TestTracker.Record("br.s.Forward", TestBrSForward() == 42);
        TestTracker.Record("br.s.SkipMultiple", TestBrSSkipMultiple() == 10);
        TestTracker.Record("br.s.InIfElse", TestBrSInIfElse(true) == 1);
        TestTracker.Record("br.s.InIfElseFalse", TestBrSInIfElse(false) == 2);
    }

    private static int TestBrSForward()
    {
        goto end;
        #pragma warning disable CS0162
        return 0;
        #pragma warning restore CS0162
        end:
        return 42;
    }

    private static int TestBrSSkipMultiple()
    {
        int x = 10;
        goto end;
        #pragma warning disable CS0162
        x = 1;
        x = 2;
        x = 3;
        #pragma warning restore CS0162
        end:
        return x;
    }

    private static int TestBrSInIfElse(bool condition)
    {
        if (condition)
            return 1;
        else
            return 2;
    }

    #endregion

    #region Section 5.4: ret (Return)

    private static void TestRet()
    {
        // Void returns
        TestTracker.Record("ret.Void", TestRetVoid());

        // Primitive returns
        TestTracker.Record("ret.Int32", TestRetInt32() == 42);
        TestTracker.Record("ret.Int32.Zero", TestRetInt32Zero() == 0);
        TestTracker.Record("ret.Int32.Neg", TestRetInt32Neg() == -123);
        TestTracker.Record("ret.Int32.Max", TestRetInt32Max() == int.MaxValue);
        TestTracker.Record("ret.Int32.Min", TestRetInt32Min() == int.MinValue);

        TestTracker.Record("ret.Int64", TestRetInt64() == 0x123456789ABCDEF0L);
        TestTracker.Record("ret.Int64.Zero", TestRetInt64Zero() == 0L);
        TestTracker.Record("ret.Int64.Max", TestRetInt64Max() == long.MaxValue);
        TestTracker.Record("ret.Int64.Min", TestRetInt64Min() == long.MinValue);

        TestTracker.Record("ret.Float32", Assert.AreApproxEqual(3.14f, TestRetFloat()));
        TestTracker.Record("ret.Float32.Zero", Assert.AreApproxEqual(0.0f, TestRetFloatZero()));
        TestTracker.Record("ret.Float64", Assert.AreApproxEqual(3.14159265358979, TestRetDouble(), 1e-10));
        TestTracker.Record("ret.Float64.Zero", Assert.AreApproxEqual(0.0, TestRetDoubleZero()));

        // Reference returns
        TestTracker.Record("ret.Object", TestRetObject() == "test");
        TestTracker.Record("ret.Null", TestRetNull() == null);
        TestTracker.Record("ret.Array", TestRetArray().Length == 3);

        // Small type returns
        TestTracker.Record("ret.Bool.True", TestRetBoolTrue());
        TestTracker.Record("ret.Bool.False", !TestRetBoolFalse());
        TestTracker.Record("ret.Byte", TestRetByte() == 255);
        TestTracker.Record("ret.SByte", TestRetSByte() == -128);
        TestTracker.Record("ret.Short", TestRetShort() == -32768);
        TestTracker.Record("ret.UShort", TestRetUShort() == 65535);
        TestTracker.Record("ret.Char", TestRetChar() == 'Z');
    }

    private static bool TestRetVoid()
    {
        ReturnVoid();
        return true;
    }

    private static void ReturnVoid() { }
    private static int TestRetInt32() => 42;
    private static int TestRetInt32Zero() => 0;
    private static int TestRetInt32Neg() => -123;
    private static int TestRetInt32Max() => int.MaxValue;
    private static int TestRetInt32Min() => int.MinValue;
    private static long TestRetInt64() => 0x123456789ABCDEF0L;
    private static long TestRetInt64Zero() => 0L;
    private static long TestRetInt64Max() => long.MaxValue;
    private static long TestRetInt64Min() => long.MinValue;
    private static float TestRetFloat() => 3.14f;
    private static float TestRetFloatZero() => 0.0f;
    private static double TestRetDouble() => 3.14159265358979;
    private static double TestRetDoubleZero() => 0.0;
    private static string TestRetObject() => "test";
    private static object? TestRetNull() => null;
    private static int[] TestRetArray() => new int[] { 1, 2, 3 };
    private static bool TestRetBoolTrue() => true;
    private static bool TestRetBoolFalse() => false;
    private static byte TestRetByte() => 255;
    private static sbyte TestRetSByte() => -128;
    private static short TestRetShort() => -32768;
    private static ushort TestRetUShort() => 65535;
    private static char TestRetChar() => 'Z';

    #endregion

    #region Section 5.2: call

    private static void TestCall()
    {
        // Static calls
        TestTracker.Record("call.Static.NoArgs", CallStaticNoArgs() == 42);
        TestTracker.Record("call.Static.OneArg", CallStaticOneArg(10) == 10);
        TestTracker.Record("call.Static.TwoArgs", CallStaticTwoArgs(10, 20) == 30);
        TestTracker.Record("call.Static.ThreeArgs", CallStaticThreeArgs(1, 2, 3) == 6);
        TestTracker.Record("call.Static.FourArgs", CallStaticFourArgs(1, 2, 3, 4) == 10);
        TestTracker.Record("call.Static.FiveArgs", CallStaticFiveArgs(1, 2, 3, 4, 5) == 15);
        TestTracker.Record("call.Static.SixArgs", CallStaticSixArgs(1, 2, 3, 4, 5, 6) == 21);

        // Mixed type arguments
        TestTracker.Record("call.MixedArgs.IntLong", CallMixedIntLong(10, 20L) == 30L);
        TestTracker.Record("call.MixedArgs.IntFloat", Assert.AreApproxEqual(30.0f, CallMixedIntFloat(10, 20.0f)));

        // Return type variations
        TestTracker.Record("call.ReturnVoid", TestCallReturnVoid());
        TestTracker.Record("call.ReturnLong", CallReturnsLong() == 0x123456789L);
        TestTracker.Record("call.ReturnFloat", Assert.AreApproxEqual(2.5f, CallReturnsFloat()));
        TestTracker.Record("call.ReturnDouble", Assert.AreApproxEqual(3.14159, CallReturnsDouble(), 1e-5));

        // Recursion
        TestTracker.Record("call.Recursive.Factorial5", Factorial(5) == 120);
        TestTracker.Record("call.Recursive.Factorial10", Factorial(10) == 3628800);
        TestTracker.Record("call.Recursive.Fib10", Fibonacci(10) == 55);

        // Nested calls
        TestTracker.Record("call.Nested", NestedCalls() == 42);
        TestTracker.Record("call.ChainedAdd", ChainedAdd(1, 2, 3, 4) == 10);

        // Pass results as arguments
        TestTracker.Record("call.PassResult", PassCallResult() == 84);
    }

    private static int CallStaticNoArgs() => StaticHelper();
    private static int StaticHelper() => 42;
    private static int CallStaticOneArg(int a) => a;
    private static int CallStaticTwoArgs(int a, int b) => a + b;
    private static int CallStaticThreeArgs(int a, int b, int c) => a + b + c;
    private static int CallStaticFourArgs(int a, int b, int c, int d) => a + b + c + d;
    private static int CallStaticFiveArgs(int a, int b, int c, int d, int e) => a + b + c + d + e;
    private static int CallStaticSixArgs(int a, int b, int c, int d, int e, int f) => a + b + c + d + e + f;

    private static long CallMixedIntLong(int a, long b) => a + b;
    private static float CallMixedIntFloat(int a, float b) => a + b;

    private static bool TestCallReturnVoid() { VoidMethod(); return true; }
    private static void VoidMethod() { }
    private static long CallReturnsLong() => 0x123456789L;
    private static float CallReturnsFloat() => 2.5f;
    private static double CallReturnsDouble() => 3.14159;

    private static int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);
    private static int Fibonacci(int n) => n <= 1 ? n : Fibonacci(n - 1) + Fibonacci(n - 2);

    private static int NestedCalls() => Level1();
    private static int Level1() => Level2();
    private static int Level2() => Level3();
    private static int Level3() => 42;

    private static int ChainedAdd(int a, int b, int c, int d) => Add(Add(a, b), Add(c, d));
    private static int Add(int a, int b) => a + b;

    private static int PassCallResult() => Double(StaticHelper());
    private static int Double(int x) => x * 2;

    #endregion

    #region Section 6: Conditional Boolean Branches

    private static void TestBrfalse()
    {
        // Integer zero tests
        TestTracker.Record("brfalse.i4.Zero", TestBrfalseInt32Zero() == 42);
        TestTracker.Record("brfalse.i4.One", TestBrfalseInt32One() == 100);
        TestTracker.Record("brfalse.i4.Neg", TestBrfalseInt32Neg() == 100);
        TestTracker.Record("brfalse.i4.Max", TestBrfalseInt32Max() == 100);
        TestTracker.Record("brfalse.i4.Min", TestBrfalseInt32Min() == 100);

        // Boolean tests
        TestTracker.Record("brfalse.False", TestBrfalseFalse() == 42);
        TestTracker.Record("brfalse.True", TestBrfalseTrue() == 100);

        // Reference tests
        TestTracker.Record("brfalse.Null", TestBrfalseNull() == 42);
        TestTracker.Record("brfalse.NonNull", TestBrfalseNonNull() == 100);
    }

    private static int TestBrfalseInt32Zero()
    {
        int x = 0;
        if (x == 0) return 42;
        return 0;
    }

    private static int TestBrfalseInt32One()
    {
        int x = 1;
        if (x == 0) return 0;
        return 100;
    }

    private static int TestBrfalseInt32Neg()
    {
        int x = -1;
        if (x == 0) return 0;
        return 100;
    }

    private static int TestBrfalseInt32Max()
    {
        int x = int.MaxValue;
        if (x == 0) return 0;
        return 100;
    }

    private static int TestBrfalseInt32Min()
    {
        int x = int.MinValue;
        if (x == 0) return 0;
        return 100;
    }

    private static int TestBrfalseFalse()
    {
        bool b = false;
        if (!b) return 42;
        return 0;
    }

    private static int TestBrfalseTrue()
    {
        bool b = true;
        if (!b) return 0;
        return 100;
    }

    private static int TestBrfalseNull()
    {
        object? obj = null;
        if (obj == null) return 42;
        return 0;
    }

    private static int TestBrfalseNonNull()
    {
        object obj = "test";
        if (obj == null) return 0;
        return 100;
    }

    private static void TestBrtrue()
    {
        // Integer non-zero tests
        TestTracker.Record("brtrue.i4.Zero", TestBrtrueInt32Zero() == 100);
        TestTracker.Record("brtrue.i4.One", TestBrtrueInt32One() == 42);
        TestTracker.Record("brtrue.i4.Neg", TestBrtrueInt32Neg() == 42);
        TestTracker.Record("brtrue.i4.Max", TestBrtrueInt32Max() == 42);
        TestTracker.Record("brtrue.i4.Min", TestBrtrueInt32Min() == 42);

        // Boolean tests
        TestTracker.Record("brtrue.True", TestBrtrueTrue() == 42);
        TestTracker.Record("brtrue.False", TestBrtrueFalse() == 100);

        // Reference tests
        TestTracker.Record("brtrue.Null", TestBrtrueNull() == 100);
        TestTracker.Record("brtrue.NonNull", TestBrtrueNonNull() == 42);
    }

    private static int TestBrtrueInt32Zero()
    {
        int x = 0;
        if (x != 0) return 0;
        return 100;
    }

    private static int TestBrtrueInt32One()
    {
        int x = 1;
        if (x != 0) return 42;
        return 0;
    }

    private static int TestBrtrueInt32Neg()
    {
        int x = -1;
        if (x != 0) return 42;
        return 0;
    }

    private static int TestBrtrueInt32Max()
    {
        int x = int.MaxValue;
        if (x != 0) return 42;
        return 0;
    }

    private static int TestBrtrueInt32Min()
    {
        int x = int.MinValue;
        if (x != 0) return 42;
        return 0;
    }

    private static int TestBrtrueTrue()
    {
        bool b = true;
        if (b) return 42;
        return 0;
    }

    private static int TestBrtrueFalse()
    {
        bool b = false;
        if (b) return 0;
        return 100;
    }

    private static int TestBrtrueNull()
    {
        object? obj = null;
        if (obj != null) return 0;
        return 100;
    }

    private static int TestBrtrueNonNull()
    {
        object obj = "test";
        if (obj != null) return 42;
        return 0;
    }

    #endregion

    #region Section 7: Conditional Comparison Branches - Signed

    private static void TestBeq()
    {
        // Int32 equality
        TestTracker.Record("beq.i4.Equal", TestBeqInt32Equal() == 42);
        TestTracker.Record("beq.i4.NotEqual", TestBeqInt32NotEqual() == 100);
        TestTracker.Record("beq.i4.ZeroZero", TestBeqInt32ZeroZero() == 42);
        TestTracker.Record("beq.i4.NegNeg", TestBeqInt32NegNeg() == 42);
        TestTracker.Record("beq.i4.MaxMax", TestBeqInt32MaxMax() == 42);
        TestTracker.Record("beq.i4.MinMin", TestBeqInt32MinMin() == 42);
        TestTracker.Record("beq.i4.MaxMin", TestBeqInt32MaxMin() == 100);
    }

    private static int TestBeqInt32Equal() { int a = 5, b = 5; if (a == b) return 42; return 0; }
    private static int TestBeqInt32NotEqual() { int a = 5, b = 10; if (a == b) return 0; return 100; }
    private static int TestBeqInt32ZeroZero() { int a = 0, b = 0; if (a == b) return 42; return 0; }
    private static int TestBeqInt32NegNeg() { int a = -5, b = -5; if (a == b) return 42; return 0; }
    private static int TestBeqInt32MaxMax() { int a = int.MaxValue, b = int.MaxValue; if (a == b) return 42; return 0; }
    private static int TestBeqInt32MinMin() { int a = int.MinValue, b = int.MinValue; if (a == b) return 42; return 0; }
    private static int TestBeqInt32MaxMin() { int a = int.MaxValue, b = int.MinValue; if (a == b) return 0; return 100; }

    private static void TestBne()
    {
        // Int32 inequality
        TestTracker.Record("bne.i4.NotEqual", TestBneInt32NotEqual() == 42);
        TestTracker.Record("bne.i4.Equal", TestBneInt32Equal() == 100);
        TestTracker.Record("bne.i4.ZeroOne", TestBneInt32ZeroOne() == 42);
        TestTracker.Record("bne.i4.NegPos", TestBneInt32NegPos() == 42);
        TestTracker.Record("bne.i4.MaxMin", TestBneInt32MaxMin() == 42);
    }

    private static int TestBneInt32NotEqual() { int a = 5, b = 10; if (a != b) return 42; return 0; }
    private static int TestBneInt32Equal() { int a = 5, b = 5; if (a != b) return 0; return 100; }
    private static int TestBneInt32ZeroOne() { int a = 0, b = 1; if (a != b) return 42; return 0; }
    private static int TestBneInt32NegPos() { int a = -5, b = 5; if (a != b) return 42; return 0; }
    private static int TestBneInt32MaxMin() { int a = int.MaxValue, b = int.MinValue; if (a != b) return 42; return 0; }

    private static void TestBlt()
    {
        // Int32 less than (signed)
        TestTracker.Record("blt.i4.Less", TestBltInt32Less() == 42);
        TestTracker.Record("blt.i4.Equal", TestBltInt32Equal() == 100);
        TestTracker.Record("blt.i4.Greater", TestBltInt32Greater() == 100);
        TestTracker.Record("blt.i4.NegLess", TestBltInt32NegLess() == 42);
        TestTracker.Record("blt.i4.NegVsPos", TestBltInt32NegVsPos() == 42);
        TestTracker.Record("blt.i4.ZeroVsNeg", TestBltInt32ZeroVsNeg() == 100);
        TestTracker.Record("blt.i4.MinVsMax", TestBltInt32MinVsMax() == 42);
        TestTracker.Record("blt.i4.MaxVsMin", TestBltInt32MaxVsMin() == 100);
    }

    private static int TestBltInt32Less() { int a = 5, b = 10; if (a < b) return 42; return 0; }
    private static int TestBltInt32Equal() { int a = 5, b = 5; if (a < b) return 0; return 100; }
    private static int TestBltInt32Greater() { int a = 10, b = 5; if (a < b) return 0; return 100; }
    private static int TestBltInt32NegLess() { int a = -10, b = -5; if (a < b) return 42; return 0; }
    private static int TestBltInt32NegVsPos() { int a = -5, b = 5; if (a < b) return 42; return 0; }
    private static int TestBltInt32ZeroVsNeg() { int a = 0, b = -1; if (a < b) return 0; return 100; }
    private static int TestBltInt32MinVsMax() { int a = int.MinValue, b = int.MaxValue; if (a < b) return 42; return 0; }
    private static int TestBltInt32MaxVsMin() { int a = int.MaxValue, b = int.MinValue; if (a < b) return 0; return 100; }

    private static void TestBle()
    {
        // Int32 less than or equal (signed)
        TestTracker.Record("ble.i4.Less", TestBleInt32Less() == 42);
        TestTracker.Record("ble.i4.Equal", TestBleInt32Equal() == 42);
        TestTracker.Record("ble.i4.Greater", TestBleInt32Greater() == 100);
        TestTracker.Record("ble.i4.NegLessOrEq", TestBleInt32NegLessOrEq() == 42);
        TestTracker.Record("ble.i4.NegVsZero", TestBleInt32NegVsZero() == 42);
        TestTracker.Record("ble.i4.MinVsMax", TestBleInt32MinVsMax() == 42);
    }

    private static int TestBleInt32Less() { int a = 5, b = 10; if (a <= b) return 42; return 0; }
    private static int TestBleInt32Equal() { int a = 5, b = 5; if (a <= b) return 42; return 0; }
    private static int TestBleInt32Greater() { int a = 10, b = 5; if (a <= b) return 0; return 100; }
    private static int TestBleInt32NegLessOrEq() { int a = -5, b = -5; if (a <= b) return 42; return 0; }
    private static int TestBleInt32NegVsZero() { int a = -1, b = 0; if (a <= b) return 42; return 0; }
    private static int TestBleInt32MinVsMax() { int a = int.MinValue, b = int.MaxValue; if (a <= b) return 42; return 0; }

    private static void TestBgt()
    {
        // Int32 greater than (signed)
        TestTracker.Record("bgt.i4.Greater", TestBgtInt32Greater() == 42);
        TestTracker.Record("bgt.i4.Equal", TestBgtInt32Equal() == 100);
        TestTracker.Record("bgt.i4.Less", TestBgtInt32Less() == 100);
        TestTracker.Record("bgt.i4.NegGreater", TestBgtInt32NegGreater() == 42);
        TestTracker.Record("bgt.i4.PosVsNeg", TestBgtInt32PosVsNeg() == 42);
        TestTracker.Record("bgt.i4.ZeroVsNeg", TestBgtInt32ZeroVsNeg() == 42);
        TestTracker.Record("bgt.i4.MaxVsMin", TestBgtInt32MaxVsMin() == 42);
    }

    private static int TestBgtInt32Greater() { int a = 10, b = 5; if (a > b) return 42; return 0; }
    private static int TestBgtInt32Equal() { int a = 5, b = 5; if (a > b) return 0; return 100; }
    private static int TestBgtInt32Less() { int a = 5, b = 10; if (a > b) return 0; return 100; }
    private static int TestBgtInt32NegGreater() { int a = -5, b = -10; if (a > b) return 42; return 0; }
    private static int TestBgtInt32PosVsNeg() { int a = 5, b = -5; if (a > b) return 42; return 0; }
    private static int TestBgtInt32ZeroVsNeg() { int a = 0, b = -1; if (a > b) return 42; return 0; }
    private static int TestBgtInt32MaxVsMin() { int a = int.MaxValue, b = int.MinValue; if (a > b) return 42; return 0; }

    private static void TestBge()
    {
        // Int32 greater than or equal (signed)
        TestTracker.Record("bge.i4.Greater", TestBgeInt32Greater() == 42);
        TestTracker.Record("bge.i4.Equal", TestBgeInt32Equal() == 42);
        TestTracker.Record("bge.i4.Less", TestBgeInt32Less() == 100);
        TestTracker.Record("bge.i4.NegGreaterOrEq", TestBgeInt32NegGreaterOrEq() == 42);
        TestTracker.Record("bge.i4.ZeroVsNeg", TestBgeInt32ZeroVsNeg() == 42);
        TestTracker.Record("bge.i4.MaxVsMin", TestBgeInt32MaxVsMin() == 42);
    }

    private static int TestBgeInt32Greater() { int a = 10, b = 5; if (a >= b) return 42; return 0; }
    private static int TestBgeInt32Equal() { int a = 5, b = 5; if (a >= b) return 42; return 0; }
    private static int TestBgeInt32Less() { int a = 5, b = 10; if (a >= b) return 0; return 100; }
    private static int TestBgeInt32NegGreaterOrEq() { int a = -5, b = -5; if (a >= b) return 42; return 0; }
    private static int TestBgeInt32ZeroVsNeg() { int a = 0, b = -1; if (a >= b) return 42; return 0; }
    private static int TestBgeInt32MaxVsMin() { int a = int.MaxValue, b = int.MinValue; if (a >= b) return 42; return 0; }

    #endregion

    #region Section 7: Conditional Comparison Branches - Unsigned

    private static void TestBltUn()
    {
        // Unsigned less than
        TestTracker.Record("blt.un.i4.Less", TestBltUnInt32Less() == 42);
        TestTracker.Record("blt.un.i4.Equal", TestBltUnInt32Equal() == 100);
        TestTracker.Record("blt.un.i4.Greater", TestBltUnInt32Greater() == 100);
        TestTracker.Record("blt.un.i4.HighBit", TestBltUnInt32HighBit() == 42);
        TestTracker.Record("blt.un.i4.ZeroVsMax", TestBltUnInt32ZeroVsMax() == 42);
        TestTracker.Record("blt.un.i4.MaxVsZero", TestBltUnInt32MaxVsZero() == 100);
    }

    private static int TestBltUnInt32Less() { uint a = 5, b = 10; if (a < b) return 42; return 0; }
    private static int TestBltUnInt32Equal() { uint a = 5, b = 5; if (a < b) return 0; return 100; }
    private static int TestBltUnInt32Greater() { uint a = 10, b = 5; if (a < b) return 0; return 100; }
    private static int TestBltUnInt32HighBit() { uint a = 0x7FFFFFFF, b = 0x80000000; if (a < b) return 42; return 0; }
    private static int TestBltUnInt32ZeroVsMax() { uint a = 0, b = 0xFFFFFFFF; if (a < b) return 42; return 0; }
    private static int TestBltUnInt32MaxVsZero() { uint a = 0xFFFFFFFF, b = 0; if (a < b) return 0; return 100; }

    private static void TestBleUn()
    {
        // Unsigned less than or equal
        TestTracker.Record("ble.un.i4.Less", TestBleUnInt32Less() == 42);
        TestTracker.Record("ble.un.i4.Equal", TestBleUnInt32Equal() == 42);
        TestTracker.Record("ble.un.i4.Greater", TestBleUnInt32Greater() == 100);
        TestTracker.Record("ble.un.i4.HighBit", TestBleUnInt32HighBit() == 42);
        TestTracker.Record("ble.un.i4.MaxVsMax", TestBleUnInt32MaxVsMax() == 42);
    }

    private static int TestBleUnInt32Less() { uint a = 5, b = 10; if (a <= b) return 42; return 0; }
    private static int TestBleUnInt32Equal() { uint a = 5, b = 5; if (a <= b) return 42; return 0; }
    private static int TestBleUnInt32Greater() { uint a = 10, b = 5; if (a <= b) return 0; return 100; }
    private static int TestBleUnInt32HighBit() { uint a = 0x7FFFFFFF, b = 0x80000000; if (a <= b) return 42; return 0; }
    private static int TestBleUnInt32MaxVsMax() { uint a = 0xFFFFFFFF, b = 0xFFFFFFFF; if (a <= b) return 42; return 0; }

    private static void TestBgtUn()
    {
        // Unsigned greater than
        TestTracker.Record("bgt.un.i4.Greater", TestBgtUnInt32Greater() == 42);
        TestTracker.Record("bgt.un.i4.Equal", TestBgtUnInt32Equal() == 100);
        TestTracker.Record("bgt.un.i4.Less", TestBgtUnInt32Less() == 100);
        TestTracker.Record("bgt.un.i4.HighBit", TestBgtUnInt32HighBit() == 42);
        TestTracker.Record("bgt.un.i4.MaxVsZero", TestBgtUnInt32MaxVsZero() == 42);
    }

    private static int TestBgtUnInt32Greater() { uint a = 10, b = 5; if (a > b) return 42; return 0; }
    private static int TestBgtUnInt32Equal() { uint a = 5, b = 5; if (a > b) return 0; return 100; }
    private static int TestBgtUnInt32Less() { uint a = 5, b = 10; if (a > b) return 0; return 100; }
    private static int TestBgtUnInt32HighBit() { uint a = 0x80000000, b = 0x7FFFFFFF; if (a > b) return 42; return 0; }
    private static int TestBgtUnInt32MaxVsZero() { uint a = 0xFFFFFFFF, b = 0; if (a > b) return 42; return 0; }

    private static void TestBgeUn()
    {
        // Unsigned greater than or equal
        TestTracker.Record("bge.un.i4.Greater", TestBgeUnInt32Greater() == 42);
        TestTracker.Record("bge.un.i4.Equal", TestBgeUnInt32Equal() == 42);
        TestTracker.Record("bge.un.i4.Less", TestBgeUnInt32Less() == 100);
        TestTracker.Record("bge.un.i4.HighBit", TestBgeUnInt32HighBit() == 42);
        TestTracker.Record("bge.un.i4.MaxVsMax", TestBgeUnInt32MaxVsMax() == 42);
    }

    private static int TestBgeUnInt32Greater() { uint a = 10, b = 5; if (a >= b) return 42; return 0; }
    private static int TestBgeUnInt32Equal() { uint a = 5, b = 5; if (a >= b) return 42; return 0; }
    private static int TestBgeUnInt32Less() { uint a = 5, b = 10; if (a >= b) return 0; return 100; }
    private static int TestBgeUnInt32HighBit() { uint a = 0x80000000, b = 0x7FFFFFFF; if (a >= b) return 42; return 0; }
    private static int TestBgeUnInt32MaxVsMax() { uint a = 0xFFFFFFFF, b = 0xFFFFFFFF; if (a >= b) return 42; return 0; }

    private static void TestBneUn()
    {
        // Unsigned not equal (same as signed for equality)
        TestTracker.Record("bne.un.i4.NotEqual", TestBneUnInt32NotEqual() == 42);
        TestTracker.Record("bne.un.i4.Equal", TestBneUnInt32Equal() == 100);
        TestTracker.Record("bne.un.i4.ZeroVsMax", TestBneUnInt32ZeroVsMax() == 42);
    }

    private static int TestBneUnInt32NotEqual() { uint a = 5, b = 10; if (a != b) return 42; return 0; }
    private static int TestBneUnInt32Equal() { uint a = 5, b = 5; if (a != b) return 0; return 100; }
    private static int TestBneUnInt32ZeroVsMax() { uint a = 0, b = 0xFFFFFFFF; if (a != b) return 42; return 0; }

    #endregion

    #region Int64 Branch Tests

    private static void TestBranchesInt64()
    {
        // Int64 equality
        TestTracker.Record("beq.i8.Equal", TestBeqInt64Equal() == 42);
        TestTracker.Record("beq.i8.NotEqual", TestBeqInt64NotEqual() == 100);
        TestTracker.Record("beq.i8.Large", TestBeqInt64Large() == 42);

        // Int64 inequality
        TestTracker.Record("bne.i8.NotEqual", TestBneInt64NotEqual() == 42);
        TestTracker.Record("bne.i8.Equal", TestBneInt64Equal() == 100);

        // Int64 less than
        TestTracker.Record("blt.i8.Less", TestBltInt64Less() == 42);
        TestTracker.Record("blt.i8.Equal", TestBltInt64Equal() == 100);
        TestTracker.Record("blt.i8.Greater", TestBltInt64Greater() == 100);
        TestTracker.Record("blt.i8.NegVsPos", TestBltInt64NegVsPos() == 42);

        // Int64 less than or equal
        TestTracker.Record("ble.i8.Less", TestBleInt64Less() == 42);
        TestTracker.Record("ble.i8.Equal", TestBleInt64Equal() == 42);
        TestTracker.Record("ble.i8.Greater", TestBleInt64Greater() == 100);

        // Int64 greater than
        TestTracker.Record("bgt.i8.Greater", TestBgtInt64Greater() == 42);
        TestTracker.Record("bgt.i8.Equal", TestBgtInt64Equal() == 100);
        TestTracker.Record("bgt.i8.Less", TestBgtInt64Less() == 100);

        // Int64 greater than or equal
        TestTracker.Record("bge.i8.Greater", TestBgeInt64Greater() == 42);
        TestTracker.Record("bge.i8.Equal", TestBgeInt64Equal() == 42);
        TestTracker.Record("bge.i8.Less", TestBgeInt64Less() == 100);

        // UInt64 unsigned comparisons
        TestTracker.Record("blt.un.i8.HighBit", TestBltUnInt64HighBit() == 42);
        TestTracker.Record("bgt.un.i8.HighBit", TestBgtUnInt64HighBit() == 42);
    }

    private static int TestBeqInt64Equal() { long a = 12345678901234L, b = 12345678901234L; if (a == b) return 42; return 0; }
    private static int TestBeqInt64NotEqual() { long a = 12345678901234L, b = 98765432109876L; if (a == b) return 0; return 100; }
    private static int TestBeqInt64Large() { long a = long.MaxValue, b = long.MaxValue; if (a == b) return 42; return 0; }

    private static int TestBneInt64NotEqual() { long a = 12345678901234L, b = 98765432109876L; if (a != b) return 42; return 0; }
    private static int TestBneInt64Equal() { long a = 12345678901234L, b = 12345678901234L; if (a != b) return 0; return 100; }

    private static int TestBltInt64Less() { long a = 12345678901234L, b = 98765432109876L; if (a < b) return 42; return 0; }
    private static int TestBltInt64Equal() { long a = 12345678901234L, b = 12345678901234L; if (a < b) return 0; return 100; }
    private static int TestBltInt64Greater() { long a = 98765432109876L, b = 12345678901234L; if (a < b) return 0; return 100; }
    private static int TestBltInt64NegVsPos() { long a = -1000000000000L, b = 1000000000000L; if (a < b) return 42; return 0; }

    private static int TestBleInt64Less() { long a = 12345678901234L, b = 98765432109876L; if (a <= b) return 42; return 0; }
    private static int TestBleInt64Equal() { long a = 12345678901234L, b = 12345678901234L; if (a <= b) return 42; return 0; }
    private static int TestBleInt64Greater() { long a = 98765432109876L, b = 12345678901234L; if (a <= b) return 0; return 100; }

    private static int TestBgtInt64Greater() { long a = 98765432109876L, b = 12345678901234L; if (a > b) return 42; return 0; }
    private static int TestBgtInt64Equal() { long a = 12345678901234L, b = 12345678901234L; if (a > b) return 0; return 100; }
    private static int TestBgtInt64Less() { long a = 12345678901234L, b = 98765432109876L; if (a > b) return 0; return 100; }

    private static int TestBgeInt64Greater() { long a = 98765432109876L, b = 12345678901234L; if (a >= b) return 42; return 0; }
    private static int TestBgeInt64Equal() { long a = 12345678901234L, b = 12345678901234L; if (a >= b) return 42; return 0; }
    private static int TestBgeInt64Less() { long a = 12345678901234L, b = 98765432109876L; if (a >= b) return 0; return 100; }

    private static int TestBltUnInt64HighBit() { ulong a = 0x7FFFFFFFFFFFFFFFUL, b = 0x8000000000000000UL; if (a < b) return 42; return 0; }
    private static int TestBgtUnInt64HighBit() { ulong a = 0x8000000000000000UL, b = 0x7FFFFFFFFFFFFFFFUL; if (a > b) return 42; return 0; }

    #endregion

    #region Float Branch Tests

    private static void TestBranchesFloat()
    {
        // Float equality
        TestTracker.Record("beq.r4.Equal", TestBeqFloatEqual() == 42);
        TestTracker.Record("beq.r4.NotEqual", TestBeqFloatNotEqual() == 100);
        TestTracker.Record("beq.r8.Equal", TestBeqDoubleEqual() == 42);
        TestTracker.Record("beq.r8.NotEqual", TestBeqDoubleNotEqual() == 100);

        // Float less than
        TestTracker.Record("blt.r4.Less", TestBltFloatLess() == 42);
        TestTracker.Record("blt.r4.Equal", TestBltFloatEqual() == 100);
        TestTracker.Record("blt.r4.Greater", TestBltFloatGreater() == 100);
        TestTracker.Record("blt.r8.Less", TestBltDoubleLess() == 42);
        TestTracker.Record("blt.r8.NegVsPos", TestBltDoubleNegVsPos() == 42);

        // Float less than or equal
        TestTracker.Record("ble.r4.Less", TestBleFloatLess() == 42);
        TestTracker.Record("ble.r4.Equal", TestBleFloatEqual() == 42);
        TestTracker.Record("ble.r8.Less", TestBleDoubleLess() == 42);
        TestTracker.Record("ble.r8.Equal", TestBleDoubleEqual() == 42);

        // Float greater than
        TestTracker.Record("bgt.r4.Greater", TestBgtFloatGreater() == 42);
        TestTracker.Record("bgt.r4.Equal", TestBgtFloatEqual() == 100);
        TestTracker.Record("bgt.r8.Greater", TestBgtDoubleGreater() == 42);

        // Float greater than or equal
        TestTracker.Record("bge.r4.Greater", TestBgeFloatGreater() == 42);
        TestTracker.Record("bge.r4.Equal", TestBgeFloatEqual() == 42);
        TestTracker.Record("bge.r8.Greater", TestBgeDoubleGreater() == 42);
        TestTracker.Record("bge.r8.Equal", TestBgeDoubleEqual() == 42);

        // Float inequality
        TestTracker.Record("bne.r4.NotEqual", TestBneFloatNotEqual() == 42);
        TestTracker.Record("bne.r4.Equal", TestBneFloatEqual() == 100);
    }

    private static int TestBeqFloatEqual() { float a = 3.14f, b = 3.14f; if (a == b) return 42; return 0; }
    private static int TestBeqFloatNotEqual() { float a = 3.14f, b = 2.71f; if (a == b) return 0; return 100; }
    private static int TestBeqDoubleEqual() { double a = 3.14159265358979, b = 3.14159265358979; if (a == b) return 42; return 0; }
    private static int TestBeqDoubleNotEqual() { double a = 3.14159, b = 2.71828; if (a == b) return 0; return 100; }

    private static int TestBltFloatLess() { float a = 1.0f, b = 2.0f; if (a < b) return 42; return 0; }
    private static int TestBltFloatEqual() { float a = 1.0f, b = 1.0f; if (a < b) return 0; return 100; }
    private static int TestBltFloatGreater() { float a = 2.0f, b = 1.0f; if (a < b) return 0; return 100; }
    private static int TestBltDoubleLess() { double a = 1.0, b = 2.0; if (a < b) return 42; return 0; }
    private static int TestBltDoubleNegVsPos() { double a = -1.5, b = 1.5; if (a < b) return 42; return 0; }

    private static int TestBleFloatLess() { float a = 1.0f, b = 2.0f; if (a <= b) return 42; return 0; }
    private static int TestBleFloatEqual() { float a = 1.0f, b = 1.0f; if (a <= b) return 42; return 0; }
    private static int TestBleDoubleLess() { double a = 1.0, b = 2.0; if (a <= b) return 42; return 0; }
    private static int TestBleDoubleEqual() { double a = 1.0, b = 1.0; if (a <= b) return 42; return 0; }

    private static int TestBgtFloatGreater() { float a = 2.0f, b = 1.0f; if (a > b) return 42; return 0; }
    private static int TestBgtFloatEqual() { float a = 1.0f, b = 1.0f; if (a > b) return 0; return 100; }
    private static int TestBgtDoubleGreater() { double a = 2.0, b = 1.0; if (a > b) return 42; return 0; }

    private static int TestBgeFloatGreater() { float a = 2.0f, b = 1.0f; if (a >= b) return 42; return 0; }
    private static int TestBgeFloatEqual() { float a = 1.0f, b = 1.0f; if (a >= b) return 42; return 0; }
    private static int TestBgeDoubleGreater() { double a = 2.0, b = 1.0; if (a >= b) return 42; return 0; }
    private static int TestBgeDoubleEqual() { double a = 1.0, b = 1.0; if (a >= b) return 42; return 0; }

    private static int TestBneFloatNotEqual() { float a = 1.0f, b = 2.0f; if (a != b) return 42; return 0; }
    private static int TestBneFloatEqual() { float a = 1.0f, b = 1.0f; if (a != b) return 0; return 100; }

    #endregion

    #region Small Type Branch Tests

    public static void RunSmallTypeBranchTests()
    {
        TestBranchesByte();
        TestBranchesSByte();
        TestBranchesShort();
        TestBranchesUShort();
        TestBranchesChar();
    }

    private static void TestBranchesByte()
    {
        // Byte equality
        TestTracker.Record("beq.byte.Equal", BeqByte(100, 100) == 42);
        TestTracker.Record("beq.byte.NotEqual", BeqByte(100, 200) == 0);
        TestTracker.Record("beq.byte.Zero", BeqByte(0, 0) == 42);
        TestTracker.Record("beq.byte.Max", BeqByte(255, 255) == 42);

        // Byte less than
        TestTracker.Record("blt.byte.Less", BltByte(50, 100) == 42);
        TestTracker.Record("blt.byte.Equal", BltByte(100, 100) == 0);
        TestTracker.Record("blt.byte.Greater", BltByte(100, 50) == 0);
        TestTracker.Record("blt.byte.ZeroMax", BltByte(0, 255) == 42);

        // Byte greater than
        TestTracker.Record("bgt.byte.Greater", BgtByte(100, 50) == 42);
        TestTracker.Record("bgt.byte.Equal", BgtByte(100, 100) == 0);
        TestTracker.Record("bgt.byte.Less", BgtByte(50, 100) == 0);

        // Byte less/greater or equal
        TestTracker.Record("ble.byte.Less", BleByte(50, 100) == 42);
        TestTracker.Record("ble.byte.Equal", BleByte(100, 100) == 42);
        TestTracker.Record("bge.byte.Greater", BgeByte(100, 50) == 42);
        TestTracker.Record("bge.byte.Equal", BgeByte(100, 100) == 42);
    }

    private static int BeqByte(byte a, byte b) { if (a == b) return 42; return 0; }
    private static int BltByte(byte a, byte b) { if (a < b) return 42; return 0; }
    private static int BgtByte(byte a, byte b) { if (a > b) return 42; return 0; }
    private static int BleByte(byte a, byte b) { if (a <= b) return 42; return 0; }
    private static int BgeByte(byte a, byte b) { if (a >= b) return 42; return 0; }

    private static void TestBranchesSByte()
    {
        // SByte equality
        TestTracker.Record("beq.sbyte.Equal", BeqSByte(50, 50) == 42);
        TestTracker.Record("beq.sbyte.NotEqual", BeqSByte(50, -50) == 0);
        TestTracker.Record("beq.sbyte.NegEqual", BeqSByte(-100, -100) == 42);
        TestTracker.Record("beq.sbyte.Zero", BeqSByte(0, 0) == 42);

        // SByte less than (signed)
        TestTracker.Record("blt.sbyte.Less", BltSByte(50, 100) == 42);
        TestTracker.Record("blt.sbyte.Equal", BltSByte(50, 50) == 0);
        TestTracker.Record("blt.sbyte.Greater", BltSByte(100, 50) == 0);
        TestTracker.Record("blt.sbyte.NegVsPos", BltSByte(-50, 50) == 42);
        TestTracker.Record("blt.sbyte.MinVsMax", BltSByte(-128, 127) == 42);
        TestTracker.Record("blt.sbyte.NegVsNeg", BltSByte(-100, -50) == 42);

        // SByte greater than (signed)
        TestTracker.Record("bgt.sbyte.Greater", BgtSByte(100, 50) == 42);
        TestTracker.Record("bgt.sbyte.Equal", BgtSByte(50, 50) == 0);
        TestTracker.Record("bgt.sbyte.PosVsNeg", BgtSByte(50, -50) == 42);
        TestTracker.Record("bgt.sbyte.MaxVsMin", BgtSByte(127, -128) == 42);

        // SByte less/greater or equal
        TestTracker.Record("ble.sbyte.Less", BleSByte(-50, 50) == 42);
        TestTracker.Record("ble.sbyte.Equal", BleSByte(50, 50) == 42);
        TestTracker.Record("bge.sbyte.Greater", BgeSByte(50, -50) == 42);
        TestTracker.Record("bge.sbyte.Equal", BgeSByte(50, 50) == 42);
    }

    private static int BeqSByte(sbyte a, sbyte b) { if (a == b) return 42; return 0; }
    private static int BltSByte(sbyte a, sbyte b) { if (a < b) return 42; return 0; }
    private static int BgtSByte(sbyte a, sbyte b) { if (a > b) return 42; return 0; }
    private static int BleSByte(sbyte a, sbyte b) { if (a <= b) return 42; return 0; }
    private static int BgeSByte(sbyte a, sbyte b) { if (a >= b) return 42; return 0; }

    private static void TestBranchesShort()
    {
        // Short equality
        TestTracker.Record("beq.short.Equal", BeqShort(1000, 1000) == 42);
        TestTracker.Record("beq.short.NotEqual", BeqShort(1000, -1000) == 0);
        TestTracker.Record("beq.short.NegEqual", BeqShort(-10000, -10000) == 42);
        TestTracker.Record("beq.short.Zero", BeqShort(0, 0) == 42);

        // Short less than (signed)
        TestTracker.Record("blt.short.Less", BltShort(1000, 2000) == 42);
        TestTracker.Record("blt.short.Equal", BltShort(1000, 1000) == 0);
        TestTracker.Record("blt.short.Greater", BltShort(2000, 1000) == 0);
        TestTracker.Record("blt.short.NegVsPos", BltShort(-5000, 5000) == 42);
        TestTracker.Record("blt.short.MinVsMax", BltShort(-32768, 32767) == 42);
        TestTracker.Record("blt.short.NegVsNeg", BltShort(-10000, -5000) == 42);

        // Short greater than (signed)
        TestTracker.Record("bgt.short.Greater", BgtShort(2000, 1000) == 42);
        TestTracker.Record("bgt.short.Equal", BgtShort(1000, 1000) == 0);
        TestTracker.Record("bgt.short.PosVsNeg", BgtShort(5000, -5000) == 42);
        TestTracker.Record("bgt.short.MaxVsMin", BgtShort(32767, -32768) == 42);

        // Short less/greater or equal
        TestTracker.Record("ble.short.Less", BleShort(-5000, 5000) == 42);
        TestTracker.Record("ble.short.Equal", BleShort(1000, 1000) == 42);
        TestTracker.Record("bge.short.Greater", BgeShort(5000, -5000) == 42);
        TestTracker.Record("bge.short.Equal", BgeShort(1000, 1000) == 42);
    }

    private static int BeqShort(short a, short b) { if (a == b) return 42; return 0; }
    private static int BltShort(short a, short b) { if (a < b) return 42; return 0; }
    private static int BgtShort(short a, short b) { if (a > b) return 42; return 0; }
    private static int BleShort(short a, short b) { if (a <= b) return 42; return 0; }
    private static int BgeShort(short a, short b) { if (a >= b) return 42; return 0; }

    private static void TestBranchesUShort()
    {
        // UShort equality
        TestTracker.Record("beq.ushort.Equal", BeqUShort(50000, 50000) == 42);
        TestTracker.Record("beq.ushort.NotEqual", BeqUShort(50000, 10000) == 0);
        TestTracker.Record("beq.ushort.Zero", BeqUShort(0, 0) == 42);
        TestTracker.Record("beq.ushort.Max", BeqUShort(65535, 65535) == 42);

        // UShort less than (unsigned)
        TestTracker.Record("blt.ushort.Less", BltUShort(10000, 50000) == 42);
        TestTracker.Record("blt.ushort.Equal", BltUShort(50000, 50000) == 0);
        TestTracker.Record("blt.ushort.Greater", BltUShort(50000, 10000) == 0);
        TestTracker.Record("blt.ushort.ZeroMax", BltUShort(0, 65535) == 42);
        TestTracker.Record("blt.ushort.HighBit", BltUShort(32767, 32768) == 42);

        // UShort greater than (unsigned)
        TestTracker.Record("bgt.ushort.Greater", BgtUShort(50000, 10000) == 42);
        TestTracker.Record("bgt.ushort.Equal", BgtUShort(50000, 50000) == 0);
        TestTracker.Record("bgt.ushort.MaxVsZero", BgtUShort(65535, 0) == 42);
        TestTracker.Record("bgt.ushort.HighBit", BgtUShort(32768, 32767) == 42);

        // UShort less/greater or equal
        TestTracker.Record("ble.ushort.Less", BleUShort(10000, 50000) == 42);
        TestTracker.Record("ble.ushort.Equal", BleUShort(50000, 50000) == 42);
        TestTracker.Record("bge.ushort.Greater", BgeUShort(50000, 10000) == 42);
        TestTracker.Record("bge.ushort.Equal", BgeUShort(50000, 50000) == 42);
    }

    private static int BeqUShort(ushort a, ushort b) { if (a == b) return 42; return 0; }
    private static int BltUShort(ushort a, ushort b) { if (a < b) return 42; return 0; }
    private static int BgtUShort(ushort a, ushort b) { if (a > b) return 42; return 0; }
    private static int BleUShort(ushort a, ushort b) { if (a <= b) return 42; return 0; }
    private static int BgeUShort(ushort a, ushort b) { if (a >= b) return 42; return 0; }

    private static void TestBranchesChar()
    {
        // Char equality
        TestTracker.Record("beq.char.Equal", BeqChar('A', 'A') == 42);
        TestTracker.Record("beq.char.NotEqual", BeqChar('A', 'B') == 0);
        TestTracker.Record("beq.char.Zero", BeqChar('\0', '\0') == 42);

        // Char less than (unsigned comparison by value)
        TestTracker.Record("blt.char.Less", BltChar('A', 'Z') == 42);
        TestTracker.Record("blt.char.Equal", BltChar('A', 'A') == 0);
        TestTracker.Record("blt.char.Greater", BltChar('Z', 'A') == 0);
        TestTracker.Record("blt.char.LowerCase", BltChar('Z', 'a') == 42); // Z=90, a=97

        // Char greater than
        TestTracker.Record("bgt.char.Greater", BgtChar('Z', 'A') == 42);
        TestTracker.Record("bgt.char.Equal", BgtChar('A', 'A') == 0);
        TestTracker.Record("bgt.char.Less", BgtChar('A', 'Z') == 0);

        // Char less/greater or equal
        TestTracker.Record("ble.char.Less", BleChar('A', 'Z') == 42);
        TestTracker.Record("ble.char.Equal", BleChar('A', 'A') == 42);
        TestTracker.Record("bge.char.Greater", BgeChar('Z', 'A') == 42);
        TestTracker.Record("bge.char.Equal", BgeChar('A', 'A') == 42);
    }

    private static int BeqChar(char a, char b) { if (a == b) return 42; return 0; }
    private static int BltChar(char a, char b) { if (a < b) return 42; return 0; }
    private static int BgtChar(char a, char b) { if (a > b) return 42; return 0; }
    private static int BleChar(char a, char b) { if (a <= b) return 42; return 0; }
    private static int BgeChar(char a, char b) { if (a >= b) return 42; return 0; }

    #endregion
}
