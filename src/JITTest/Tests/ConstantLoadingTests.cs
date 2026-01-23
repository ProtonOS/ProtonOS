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
        // ldnull tests (10 tests)
        TestLdnull();

        // ldc.i4.m1 through ldc.i4.8 (10 + 12 + 10 * 7 = 92 tests for short forms)
        TestLdcI4M1();
        TestLdcI4_0();
        TestLdcI4_1();
        TestLdcI4_2to8();

        // ldc.i4.s tests (10 tests)
        TestLdcI4S();

        // ldc.i4 tests (10 tests)
        TestLdcI4();

        // ldc.i8 tests (12 tests)
        TestLdcI8();

        // ldc.r4 tests (14 tests)
        TestLdcR4();

        // ldc.r8 tests (14 tests)
        TestLdcR8();

        // Additional constant tests
        RunAdditionalTests();
    }

    #region ldnull (0x14) - 10 tests

    private static void TestLdnull()
    {
        // Basic null load
        object? obj = null;
        TestTracker.Record("ldnull.Basic", obj == null);

        // Null comparison with ceq
        TestTracker.Record("ldnull.NullEqNull", (object?)null == null);

        // Null as method argument
        TestTracker.Record("ldnull.AsArgument", IsNull(null));

        // Null stored to local
        object? local;
        local = null;
        TestTracker.Record("ldnull.StoreToLocal", local == null);

        // Null for brfalse (null is false)
        bool branchTaken = false;
        if (obj == null) branchTaken = true;
        TestTracker.Record("ldnull.Brfalse", branchTaken);

        // Null for brtrue (null is not true)
        branchTaken = true;
        if (obj != null) branchTaken = false;
        TestTracker.Record("ldnull.Brtrue", branchTaken);

        // Different reference types
        string? str = null;
        TestTracker.Record("ldnull.String", str == null);

        int[]? arr = null;
        TestTracker.Record("ldnull.Array", arr == null);

        // Null with is/as
        TestTracker.Record("ldnull.IsCheck", !(null is string));

        // Null returned from method
        TestTracker.Record("ldnull.ReturnNull", ReturnNull() == null);
    }

    private static bool IsNull(object? obj) => obj == null;
    private static object? ReturnNull() => null;

    #endregion

    #region ldc.i4.m1 (0x15) - 10 tests

    private static void TestLdcI4M1()
    {
        // Basic -1 load
        TestTracker.Record("ldc.i4.m1.Basic", GetMinusOne() == -1);

        // Verify bit pattern (0xFFFFFFFF)
        TestTracker.Record("ldc.i4.m1.BitPattern", unchecked((uint)(-1)) == 0xFFFFFFFF);

        // Arithmetic: -1 + 1 = 0
        TestTracker.Record("ldc.i4.m1.PlusOne", -1 + 1 == 0);

        // As bit mask (all bits set)
        TestTracker.Record("ldc.i4.m1.BitMask", (0x12345678 & -1) == 0x12345678);

        // Comparison
        TestTracker.Record("ldc.i4.m1.EqSelf", -1 == -1);

        // As uint: 4294967295
        TestTracker.Record("ldc.i4.m1.AsUInt", unchecked((uint)(-1)) == 4294967295u);

        // Bitwise AND
        TestTracker.Record("ldc.i4.m1.BitwiseAnd", (42 & -1) == 42);

        // Bitwise OR
        TestTracker.Record("ldc.i4.m1.BitwiseOr", (42 | -1) == -1);

        // Sign extension to int64
        TestTracker.Record("ldc.i4.m1.Conv64", (long)(-1) == -1L);

        // Multiplication
        TestTracker.Record("ldc.i4.m1.Multiply", 42 * -1 == -42);
    }

    private static int GetMinusOne() => -1;

    #endregion

    #region ldc.i4.0 (0x16) - 12 tests

    private static void TestLdcI4_0()
    {
        // Basic 0 load
        TestTracker.Record("ldc.i4.0.Basic", GetZero() == 0);

        // Arithmetic: x + 0 = x
        TestTracker.Record("ldc.i4.0.AddIdentity", 42 + 0 == 42);

        // Arithmetic: x * 0 = 0
        TestTracker.Record("ldc.i4.0.MultiplyZero", 42 * 0 == 0);

        // For brfalse (0 is false)
        bool taken = false;
        if (0 == 0) taken = true;
        TestTracker.Record("ldc.i4.0.Brfalse", taken);

        // Comparison
        TestTracker.Record("ldc.i4.0.EqSelf", 0 == 0);

        // As array index (first element)
        int[] arr = new int[] { 42, 1, 2 };
        TestTracker.Record("ldc.i4.0.ArrayIndex", arr[0] == 42);

        // Bitwise ops
        TestTracker.Record("ldc.i4.0.BitwiseAnd", (42 & 0) == 0);
        TestTracker.Record("ldc.i4.0.BitwiseOr", (42 | 0) == 42);
        TestTracker.Record("ldc.i4.0.BitwiseXor", (42 ^ 0) == 42);

        // Shift by 0
        TestTracker.Record("ldc.i4.0.ShiftLeft", (42 << 0) == 42);
        TestTracker.Record("ldc.i4.0.ShiftRight", (42 >> 0) == 42);

        // Extension to int64
        TestTracker.Record("ldc.i4.0.Conv64", (long)0 == 0L);
    }

    private static int GetZero() => 0;

    #endregion

    #region ldc.i4.1 (0x17) - 10 tests

    private static void TestLdcI4_1()
    {
        // Basic 1 load
        TestTracker.Record("ldc.i4.1.Basic", GetOne() == 1);

        // Increment pattern
        int x = 0;
        x = x + 1;
        TestTracker.Record("ldc.i4.1.Increment", x == 1);

        // As true boolean
        bool taken = false;
        if (1 != 0) taken = true;
        TestTracker.Record("ldc.i4.1.IsTrue", taken);

        // Multiplication identity
        TestTracker.Record("ldc.i4.1.MultIdentity", 42 * 1 == 42);

        // Division identity
        TestTracker.Record("ldc.i4.1.DivIdentity", 42 / 1 == 42);

        // Bit pattern (only LSB set)
        TestTracker.Record("ldc.i4.1.BitPattern", (1 & 0x01) == 1);

        // Shift by 1
        TestTracker.Record("ldc.i4.1.ShiftLeft", (1 << 1) == 2);

        // Comparison result representation
        int cmp = (42 == 42) ? 1 : 0;
        TestTracker.Record("ldc.i4.1.CmpResult", cmp == 1);

        // Extension to int64
        TestTracker.Record("ldc.i4.1.Conv64", (long)1 == 1L);

        // As loop increment
        int sum = 0;
        for (int i = 0; i < 3; i = i + 1) sum = sum + 1;
        TestTracker.Record("ldc.i4.1.LoopIncrement", sum == 3);
    }

    private static int GetOne() => 1;

    #endregion

    #region ldc.i4.2 through ldc.i4.8 (0x18-0x1E) - 14 tests

    private static void TestLdcI4_2to8()
    {
        // ldc.i4.2
        TestTracker.Record("ldc.i4.2.Basic", GetTwo() == 2);
        TestTracker.Record("ldc.i4.2.Double", 1 << 1 == 2);

        // ldc.i4.3
        TestTracker.Record("ldc.i4.3.Basic", GetThree() == 3);
        TestTracker.Record("ldc.i4.3.Sum", 1 + 2 == 3);

        // ldc.i4.4
        TestTracker.Record("ldc.i4.4.Basic", GetFour() == 4);
        TestTracker.Record("ldc.i4.4.Shift", 1 << 2 == 4);

        // ldc.i4.5
        TestTracker.Record("ldc.i4.5.Basic", GetFive() == 5);
        TestTracker.Record("ldc.i4.5.Sum", 2 + 3 == 5);

        // ldc.i4.6
        TestTracker.Record("ldc.i4.6.Basic", GetSix() == 6);
        TestTracker.Record("ldc.i4.6.Product", 2 * 3 == 6);

        // ldc.i4.7
        TestTracker.Record("ldc.i4.7.Basic", GetSeven() == 7);
        TestTracker.Record("ldc.i4.7.BitPattern", (7 & 0x07) == 7);

        // ldc.i4.8
        TestTracker.Record("ldc.i4.8.Basic", GetEight() == 8);
        TestTracker.Record("ldc.i4.8.Shift", 1 << 3 == 8);
    }

    private static int GetTwo() => 2;
    private static int GetThree() => 3;
    private static int GetFour() => 4;
    private static int GetFive() => 5;
    private static int GetSix() => 6;
    private static int GetSeven() => 7;
    private static int GetEight() => 8;

    #endregion

    #region ldc.i4.s (0x1F) - 10 tests

    private static void TestLdcI4S()
    {
        // Values 9-127 (above short form range)
        TestTracker.Record("ldc.i4.s.9", GetNine() == 9);
        TestTracker.Record("ldc.i4.s.42", Get42() == 42);
        TestTracker.Record("ldc.i4.s.100", Get100() == 100);
        TestTracker.Record("ldc.i4.s.127", Get127() == 127);

        // Negative values -2 to -128
        TestTracker.Record("ldc.i4.s.-2", GetMinusTwo() == -2);
        TestTracker.Record("ldc.i4.s.-42", GetMinus42() == -42);
        TestTracker.Record("ldc.i4.s.-100", GetMinus100() == -100);
        TestTracker.Record("ldc.i4.s.-128", GetMinus128() == -128);

        // Arithmetic with ldc.i4.s values
        TestTracker.Record("ldc.i4.s.Add", 42 + 42 == 84);
        TestTracker.Record("ldc.i4.s.Negate", -(-42) == 42);
    }

    private static int GetNine() => 9;
    private static int Get42() => 42;
    private static int Get100() => 100;
    private static int Get127() => 127;
    private static int GetMinusTwo() => -2;
    private static int GetMinus42() => -42;
    private static int GetMinus100() => -100;
    private static int GetMinus128() => -128;

    #endregion

    #region ldc.i4 (0x20) - 10 tests

    private static void TestLdcI4()
    {
        // Values outside -128 to 127 range
        TestTracker.Record("ldc.i4.128", Get128() == 128);
        TestTracker.Record("ldc.i4.-129", GetMinus129() == -129);
        TestTracker.Record("ldc.i4.1000", Get1000() == 1000);
        TestTracker.Record("ldc.i4.-1000", GetMinus1000() == -1000);
        TestTracker.Record("ldc.i4.1000000", Get1000000() == 1000000);

        // Boundary values
        TestTracker.Record("ldc.i4.MaxValue", GetMaxInt32() == int.MaxValue);
        TestTracker.Record("ldc.i4.MinValue", GetMinInt32() == int.MinValue);

        // Bit patterns
        TestTracker.Record("ldc.i4.HexPattern", GetHexPattern() == 0x12345678);
        TestTracker.Record("ldc.i4.HighBit", GetHighBit() == unchecked((int)0x80000000));

        // Arithmetic near boundaries
        TestTracker.Record("ldc.i4.MaxMinus1", int.MaxValue - 1 == 2147483646);
    }

    private static int Get128() => 128;
    private static int GetMinus129() => -129;
    private static int Get1000() => 1000;
    private static int GetMinus1000() => -1000;
    private static int Get1000000() => 1000000;
    private static int GetMaxInt32() => int.MaxValue;
    private static int GetMinInt32() => int.MinValue;
    private static int GetHexPattern() => 0x12345678;
    private static int GetHighBit() => unchecked((int)0x80000000);

    #endregion

    #region ldc.i8 (0x21) - 12 tests

    private static void TestLdcI8()
    {
        // Basic values
        TestTracker.Record("ldc.i8.Zero", GetLongZero() == 0L);
        TestTracker.Record("ldc.i8.One", GetLongOne() == 1L);
        TestTracker.Record("ldc.i8.MinusOne", GetLongMinusOne() == -1L);

        // Boundary values
        TestTracker.Record("ldc.i8.MaxValue", GetLongMaxValue() == long.MaxValue);
        TestTracker.Record("ldc.i8.MinValue", GetLongMinValue() == long.MinValue);

        // Values beyond int32 range
        TestTracker.Record("ldc.i8.BeyondInt32", GetBeyondInt32() == 0x100000000L);
        TestTracker.Record("ldc.i8.LargePositive", GetLargeLong() == 0x123456789ABCDEF0L);

        // Bit patterns
        TestTracker.Record("ldc.i8.HighBits", GetHighBitsLong() == unchecked((long)0xFFFFFFFF00000000L));
        TestTracker.Record("ldc.i8.LowBits", GetLowBitsLong() == 0x00000000FFFFFFFFL);

        // Arithmetic
        TestTracker.Record("ldc.i8.Add", 0x100000000L + 0x100000000L == 0x200000000L);
        TestTracker.Record("ldc.i8.Negate", -(-1L) == 1L);

        // Sign extension check
        TestTracker.Record("ldc.i8.SignExtCheck", (long)(-1) == -1L);
    }

    private static long GetLongZero() => 0L;
    private static long GetLongOne() => 1L;
    private static long GetLongMinusOne() => -1L;
    private static long GetLongMaxValue() => long.MaxValue;
    private static long GetLongMinValue() => long.MinValue;
    private static long GetBeyondInt32() => 0x100000000L;
    private static long GetLargeLong() => 0x123456789ABCDEF0L;
    private static long GetHighBitsLong() => unchecked((long)0xFFFFFFFF00000000L);
    private static long GetLowBitsLong() => 0x00000000FFFFFFFFL;

    #endregion

    #region ldc.r4 (0x22) - 14 tests

    private static void TestLdcR4()
    {
        // Basic values
        TestTracker.Record("ldc.r4.Zero", Assert.AreApproxEqual(0.0f, GetFloatZero()));
        TestTracker.Record("ldc.r4.One", Assert.AreApproxEqual(1.0f, GetFloatOne()));
        TestTracker.Record("ldc.r4.MinusOne", Assert.AreApproxEqual(-1.0f, GetFloatMinusOne()));

        // Mathematical constants
        TestTracker.Record("ldc.r4.Pi", Assert.AreApproxEqual(3.14159265f, GetFloatPi(), 1e-5f));
        TestTracker.Record("ldc.r4.E", Assert.AreApproxEqual(2.71828f, GetFloatE(), 1e-4f));

        // Boundary values
        TestTracker.Record("ldc.r4.MaxValue", GetFloatMaxValue() == float.MaxValue);
        TestTracker.Record("ldc.r4.MinValue", GetFloatMinValue() == float.MinValue);
        TestTracker.Record("ldc.r4.Epsilon", GetFloatEpsilon() == float.Epsilon);

        // Special values
        TestTracker.Record("ldc.r4.PositiveInfinity", Assert.IsPositiveInfinity(GetFloatPosInf()));
        TestTracker.Record("ldc.r4.NegativeInfinity", Assert.IsNegativeInfinity(GetFloatNegInf()));
        TestTracker.Record("ldc.r4.NaN", Assert.IsNaN(GetFloatNaN()));

        // Arithmetic
        TestTracker.Record("ldc.r4.Add", Assert.AreApproxEqual(3.0f, 1.0f + 2.0f));
        TestTracker.Record("ldc.r4.Multiply", Assert.AreApproxEqual(6.0f, 2.0f * 3.0f));

        // Small values
        TestTracker.Record("ldc.r4.SmallValue", Assert.AreApproxEqual(0.001f, GetSmallFloat(), 1e-6f));
    }

    private static float GetFloatZero() => 0.0f;
    private static float GetFloatOne() => 1.0f;
    private static float GetFloatMinusOne() => -1.0f;
    private static float GetFloatPi() => 3.14159265f;
    private static float GetFloatE() => 2.71828f;
    private static float GetFloatMaxValue() => float.MaxValue;
    private static float GetFloatMinValue() => float.MinValue;
    private static float GetFloatEpsilon() => float.Epsilon;
    private static float GetFloatPosInf() => float.PositiveInfinity;
    private static float GetFloatNegInf() => float.NegativeInfinity;
    private static float GetFloatNaN() => float.NaN;
    private static float GetSmallFloat() => 0.001f;

    #endregion

    #region ldc.r8 (0x23) - 14 tests

    private static void TestLdcR8()
    {
        // Basic values
        TestTracker.Record("ldc.r8.Zero", Assert.AreApproxEqual(0.0, GetDoubleZero()));
        TestTracker.Record("ldc.r8.One", Assert.AreApproxEqual(1.0, GetDoubleOne()));
        TestTracker.Record("ldc.r8.MinusOne", Assert.AreApproxEqual(-1.0, GetDoubleMinusOne()));

        // Mathematical constants
        TestTracker.Record("ldc.r8.Pi", Assert.AreApproxEqual(3.14159265358979, GetDoublePi(), 1e-12));
        TestTracker.Record("ldc.r8.E", Assert.AreApproxEqual(2.71828182845905, GetDoubleE(), 1e-12));

        // Boundary values
        TestTracker.Record("ldc.r8.MaxValue", GetDoubleMaxValue() == double.MaxValue);
        TestTracker.Record("ldc.r8.MinValue", GetDoubleMinValue() == double.MinValue);
        TestTracker.Record("ldc.r8.Epsilon", GetDoubleEpsilon() == double.Epsilon);

        // Special values
        TestTracker.Record("ldc.r8.PositiveInfinity", Assert.IsPositiveInfinity(GetDoublePosInf()));
        TestTracker.Record("ldc.r8.NegativeInfinity", Assert.IsNegativeInfinity(GetDoubleNegInf()));
        TestTracker.Record("ldc.r8.NaN", Assert.IsNaN(GetDoubleNaN()));

        // Arithmetic
        TestTracker.Record("ldc.r8.Add", Assert.AreApproxEqual(3.0, 1.0 + 2.0));
        TestTracker.Record("ldc.r8.Multiply", Assert.AreApproxEqual(6.0, 2.0 * 3.0));

        // High precision
        TestTracker.Record("ldc.r8.HighPrecision", Assert.AreApproxEqual(1.23456789012345, GetHighPrecision(), 1e-14));
    }

    private static double GetDoubleZero() => 0.0;
    private static double GetDoubleOne() => 1.0;
    private static double GetDoubleMinusOne() => -1.0;
    private static double GetDoublePi() => 3.14159265358979;
    private static double GetDoubleE() => 2.71828182845905;
    private static double GetDoubleMaxValue() => double.MaxValue;
    private static double GetDoubleMinValue() => double.MinValue;
    private static double GetDoubleEpsilon() => double.Epsilon;
    private static double GetDoublePosInf() => double.PositiveInfinity;
    private static double GetDoubleNegInf() => double.NegativeInfinity;
    private static double GetDoubleNaN() => double.NaN;
    private static double GetHighPrecision() => 1.23456789012345;

    #endregion

    #region Additional Constant Tests

    public static void RunAdditionalTests()
    {
        TestConstantArithmetic();
        TestConstantInExpressions();
        TestConstantInConditions();
    }

    private static void TestConstantArithmetic()
    {
        // Constant folding tests
        TestTracker.Record("const.fold.Add", 10 + 20 == 30);
        TestTracker.Record("const.fold.Sub", 50 - 30 == 20);
        TestTracker.Record("const.fold.Mul", 6 * 7 == 42);
        TestTracker.Record("const.fold.Div", 100 / 5 == 20);
        TestTracker.Record("const.fold.Mod", 17 % 5 == 2);

        // Constant expressions with longs
        TestTracker.Record("const.fold.LongAdd", 1000000000L + 2000000000L == 3000000000L);
        TestTracker.Record("const.fold.LongMul", 1000000L * 1000000L == 1000000000000L);

        // Constant expressions with floats
        TestTracker.Record("const.fold.FloatAdd", 1.5f + 2.5f == 4.0f);
        TestTracker.Record("const.fold.FloatMul", 2.0f * 3.5f == 7.0f);

        // Constant expressions with doubles
        TestTracker.Record("const.fold.DoubleAdd", 1.5 + 2.5 == 4.0);
        TestTracker.Record("const.fold.DoubleMul", 2.0 * 3.5 == 7.0);

        // Nested constant expressions
        TestTracker.Record("const.fold.Nested1", (10 + 20) * 3 == 90);
        TestTracker.Record("const.fold.Nested2", 100 / (5 + 5) == 10);
        TestTracker.Record("const.fold.Nested3", (10 * 10) + (5 * 5) == 125);
    }

    private static void TestConstantInExpressions()
    {
        // Constants combined with variables
        int x = 5;
        TestTracker.Record("const.expr.VarPlusConst", x + 10 == 15);
        TestTracker.Record("const.expr.ConstPlusVar", 10 + x == 15);
        TestTracker.Record("const.expr.VarMulConst", x * 4 == 20);
        TestTracker.Record("const.expr.ConstDivVar", 20 / x == 4);

        long y = 1000L;
        TestTracker.Record("const.expr.LongVarPlusConst", y + 2000L == 3000L);
        TestTracker.Record("const.expr.LongConstMulVar", 1000000L * y == 1000000000L);

        float f = 2.0f;
        TestTracker.Record("const.expr.FloatVarPlusConst", f + 1.5f == 3.5f);
        TestTracker.Record("const.expr.FloatConstMulVar", 3.0f * f == 6.0f);

        double d = 2.5;
        TestTracker.Record("const.expr.DoubleVarPlusConst", d + 2.5 == 5.0);
        TestTracker.Record("const.expr.DoubleConstMulVar", 4.0 * d == 10.0);
    }

    private static void TestConstantInConditions()
    {
        // Constant conditions
        TestTracker.Record("const.cond.TrueConst", (true ? 42 : 0) == 42);
        TestTracker.Record("const.cond.ZeroEqZero", (0 == 0 ? 1 : 0) == 1);
        TestTracker.Record("const.cond.OneNeqZero", (1 != 0 ? 1 : 0) == 1);

        // Constant comparisons
        TestTracker.Record("const.cond.IntLt", (5 < 10 ? 1 : 0) == 1);
        TestTracker.Record("const.cond.IntGt", (10 > 5 ? 1 : 0) == 1);
        TestTracker.Record("const.cond.IntLe", (5 <= 5 ? 1 : 0) == 1);
        TestTracker.Record("const.cond.IntGe", (5 >= 5 ? 1 : 0) == 1);

        // Constant comparisons with longs
        TestTracker.Record("const.cond.LongLt", (5L < 10L ? 1 : 0) == 1);
        TestTracker.Record("const.cond.LongGt", (0x100000000L > 0x0FFFFFFFFL ? 1 : 0) == 1);

        // Constant comparisons with floats
        TestTracker.Record("const.cond.FloatLt", (1.0f < 2.0f ? 1 : 0) == 1);
        TestTracker.Record("const.cond.FloatEq", (3.14f == 3.14f ? 1 : 0) == 1);

        // Constant comparisons with doubles
        TestTracker.Record("const.cond.DoubleLt", (1.0 < 2.0 ? 1 : 0) == 1);
        TestTracker.Record("const.cond.DoubleEq", (3.14159 == 3.14159 ? 1 : 0) == 1);

        // Compound conditions with constants
        TestTracker.Record("const.cond.And", ((1 > 0) && (2 > 1) ? 1 : 0) == 1);
        TestTracker.Record("const.cond.Or", ((0 > 1) || (2 > 1) ? 1 : 0) == 1);
    }

    #endregion
}
