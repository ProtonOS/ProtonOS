// JITTest - Constant Loading Tests
// Section 3: ldnull (0x14), ldc.i4.* (0x15-0x21), ldc.i8 (0x21), ldc.r4 (0x22), ldc.r8 (0x23)

namespace JITTest;

/// <summary>
/// Tests for constant loading instructions
/// </summary>
public static class ConstantLoadingTests
{
    public static void RunAll()
    {
        // ldnull tests
        TestLdnull();

        // ldc.i4.m1 through ldc.i4.8
        TestLdcI4ShortForm();

        // ldc.i4.s tests
        TestLdcI4S();

        // ldc.i4 tests
        TestLdcI4();

        // ldc.i8 tests
        TestLdcI8();

        // ldc.r4 tests
        TestLdcR4();

        // ldc.r8 tests
        TestLdcR8();
    }

    #region ldnull (0x14)

    private static void TestLdnull()
    {
        object? obj = null;
        TestTracker.Record("ldnull.Object", obj == null);

        string? str = null;
        TestTracker.Record("ldnull.String", str == null);

        int[]? arr = null;
        TestTracker.Record("ldnull.Array", arr == null);

        // Null comparison
        TestTracker.Record("ldnull.Comparison", (object?)null == null);
    }

    #endregion

    #region ldc.i4.m1 through ldc.i4.8 (0x15-0x1E)

    private static void TestLdcI4ShortForm()
    {
        // ldc.i4.m1 (0x15)
        TestTracker.Record("ldc.i4.m1", GetMinusOne() == -1);

        // ldc.i4.0 through ldc.i4.8 (0x16-0x1E)
        TestTracker.Record("ldc.i4.0", GetZero() == 0);
        TestTracker.Record("ldc.i4.1", GetOne() == 1);
        TestTracker.Record("ldc.i4.2", GetTwo() == 2);
        TestTracker.Record("ldc.i4.3", GetThree() == 3);
        TestTracker.Record("ldc.i4.4", GetFour() == 4);
        TestTracker.Record("ldc.i4.5", GetFive() == 5);
        TestTracker.Record("ldc.i4.6", GetSix() == 6);
        TestTracker.Record("ldc.i4.7", GetSeven() == 7);
        TestTracker.Record("ldc.i4.8", GetEight() == 8);
    }

    private static int GetMinusOne() => -1;
    private static int GetZero() => 0;
    private static int GetOne() => 1;
    private static int GetTwo() => 2;
    private static int GetThree() => 3;
    private static int GetFour() => 4;
    private static int GetFive() => 5;
    private static int GetSix() => 6;
    private static int GetSeven() => 7;
    private static int GetEight() => 8;

    #endregion

    #region ldc.i4.s (0x1F)

    private static void TestLdcI4S()
    {
        // ldc.i4.s is used for -128 to 127 (except -1 to 8)
        TestTracker.Record("ldc.i4.s.9", GetNine() == 9);
        TestTracker.Record("ldc.i4.s.127", Get127() == 127);
        TestTracker.Record("ldc.i4.s.-2", GetMinusTwo() == -2);
        TestTracker.Record("ldc.i4.s.-128", GetMinus128() == -128);
        TestTracker.Record("ldc.i4.s.42", Get42() == 42);
    }

    private static int GetNine() => 9;
    private static int Get127() => 127;
    private static int GetMinusTwo() => -2;
    private static int GetMinus128() => -128;
    private static int Get42() => 42;

    #endregion

    #region ldc.i4 (0x20)

    private static void TestLdcI4()
    {
        // ldc.i4 is used for values outside -128 to 127
        TestTracker.Record("ldc.i4.128", Get128() == 128);
        TestTracker.Record("ldc.i4.-129", GetMinus129() == -129);
        TestTracker.Record("ldc.i4.MaxValue", GetMaxInt32() == int.MaxValue);
        TestTracker.Record("ldc.i4.MinValue", GetMinInt32() == int.MinValue);
        TestTracker.Record("ldc.i4.1000000", Get1000000() == 1000000);
    }

    private static int Get128() => 128;
    private static int GetMinus129() => -129;
    private static int GetMaxInt32() => int.MaxValue;
    private static int GetMinInt32() => int.MinValue;
    private static int Get1000000() => 1000000;

    #endregion

    #region ldc.i8 (0x21)

    private static void TestLdcI8()
    {
        TestTracker.Record("ldc.i8.Zero", GetLongZero() == 0L);
        TestTracker.Record("ldc.i8.One", GetLongOne() == 1L);
        TestTracker.Record("ldc.i8.MinusOne", GetLongMinusOne() == -1L);
        TestTracker.Record("ldc.i8.MaxValue", GetLongMaxValue() == long.MaxValue);
        TestTracker.Record("ldc.i8.MinValue", GetLongMinValue() == long.MinValue);
        TestTracker.Record("ldc.i8.LargePositive", GetLargeLong() == 0x123456789ABCDEF0L);
        TestTracker.Record("ldc.i8.BeyondInt32", GetBeyondInt32() == 0x100000000L);
    }

    private static long GetLongZero() => 0L;
    private static long GetLongOne() => 1L;
    private static long GetLongMinusOne() => -1L;
    private static long GetLongMaxValue() => long.MaxValue;
    private static long GetLongMinValue() => long.MinValue;
    private static long GetLargeLong() => 0x123456789ABCDEF0L;
    private static long GetBeyondInt32() => 0x100000000L;

    #endregion

    #region ldc.r4 (0x22)

    private static void TestLdcR4()
    {
        TestTracker.Record("ldc.r4.Zero", Assert.AreApproxEqual(0.0f, GetFloatZero()));
        TestTracker.Record("ldc.r4.One", Assert.AreApproxEqual(1.0f, GetFloatOne()));
        TestTracker.Record("ldc.r4.MinusOne", Assert.AreApproxEqual(-1.0f, GetFloatMinusOne()));
        TestTracker.Record("ldc.r4.Pi", Assert.AreApproxEqual(3.14159265f, GetFloatPi(), 1e-5f));
        TestTracker.Record("ldc.r4.MaxValue", GetFloatMaxValue() == float.MaxValue);
        TestTracker.Record("ldc.r4.MinValue", GetFloatMinValue() == float.MinValue);
        TestTracker.Record("ldc.r4.Epsilon", GetFloatEpsilon() == float.Epsilon);
        TestTracker.Record("ldc.r4.PositiveInfinity", Assert.IsPositiveInfinity(GetFloatPosInf()));
        TestTracker.Record("ldc.r4.NegativeInfinity", Assert.IsNegativeInfinity(GetFloatNegInf()));
        TestTracker.Record("ldc.r4.NaN", Assert.IsNaN(GetFloatNaN()));
    }

    private static float GetFloatZero() => 0.0f;
    private static float GetFloatOne() => 1.0f;
    private static float GetFloatMinusOne() => -1.0f;
    private static float GetFloatPi() => 3.14159265f;
    private static float GetFloatMaxValue() => float.MaxValue;
    private static float GetFloatMinValue() => float.MinValue;
    private static float GetFloatEpsilon() => float.Epsilon;
    private static float GetFloatPosInf() => float.PositiveInfinity;
    private static float GetFloatNegInf() => float.NegativeInfinity;
    private static float GetFloatNaN() => float.NaN;

    #endregion

    #region ldc.r8 (0x23)

    private static void TestLdcR8()
    {
        TestTracker.Record("ldc.r8.Zero", Assert.AreApproxEqual(0.0, GetDoubleZero()));
        TestTracker.Record("ldc.r8.One", Assert.AreApproxEqual(1.0, GetDoubleOne()));
        TestTracker.Record("ldc.r8.MinusOne", Assert.AreApproxEqual(-1.0, GetDoubleMinusOne()));
        TestTracker.Record("ldc.r8.Pi", Assert.AreApproxEqual(3.14159265358979, GetDoublePi(), 1e-12));
        TestTracker.Record("ldc.r8.MaxValue", GetDoubleMaxValue() == double.MaxValue);
        TestTracker.Record("ldc.r8.MinValue", GetDoubleMinValue() == double.MinValue);
        TestTracker.Record("ldc.r8.Epsilon", GetDoubleEpsilon() == double.Epsilon);
        TestTracker.Record("ldc.r8.PositiveInfinity", Assert.IsPositiveInfinity(GetDoublePosInf()));
        TestTracker.Record("ldc.r8.NegativeInfinity", Assert.IsNegativeInfinity(GetDoubleNegInf()));
        TestTracker.Record("ldc.r8.NaN", Assert.IsNaN(GetDoubleNaN()));
    }

    private static double GetDoubleZero() => 0.0;
    private static double GetDoubleOne() => 1.0;
    private static double GetDoubleMinusOne() => -1.0;
    private static double GetDoublePi() => 3.14159265358979;
    private static double GetDoubleMaxValue() => double.MaxValue;
    private static double GetDoubleMinValue() => double.MinValue;
    private static double GetDoubleEpsilon() => double.Epsilon;
    private static double GetDoublePosInf() => double.PositiveInfinity;
    private static double GetDoubleNegInf() => double.NegativeInfinity;
    private static double GetDoubleNaN() => double.NaN;

    #endregion
}
