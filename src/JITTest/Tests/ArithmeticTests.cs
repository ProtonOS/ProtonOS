// JITTest - Arithmetic Operation Tests
// Section 11: add, sub, mul, div, rem, neg, and overflow variants

namespace JITTest;

/// <summary>
/// Tests for arithmetic instructions
/// </summary>
public static class ArithmeticTests
{
    public static void RunAll()
    {
        // Basic arithmetic
        TestAdd();
        TestSub();
        TestMul();
        TestDiv();
        TestRem();
        TestNeg();

        // Overflow variants
        TestAddOvf();
        TestSubOvf();
        TestMulOvf();

        // Unsigned variants
        TestDivUn();
        TestRemUn();
        TestAddOvfUn();
        TestMulOvfUn();
        TestSubOvfUn();

        // Small type arithmetic (byte, sbyte, short, ushort)
        RunSmallTypeTests();
    }

    #region add (0x58)

    private static void TestAdd()
    {
        // int32 tests - basic
        TestTracker.Record("add.i4.ZeroZero", Add32(0, 0) == 0);
        TestTracker.Record("add.i4.OnePlusOne", Add32(1, 1) == 2);
        TestTracker.Record("add.i4.Simple", Add32(1, 2) == 3);
        TestTracker.Record("add.i4.Negative", Add32(-5, 3) == -2);
        TestTracker.Record("add.i4.OppositeSigns", Add32(-1, 1) == 0);
        TestTracker.Record("add.i4.MaxPlusZero", Add32(int.MaxValue, 0) == int.MaxValue);
        TestTracker.Record("add.i4.MinPlusZero", Add32(int.MinValue, 0) == int.MinValue);

        // int32 overflow wrapping (unchecked)
        TestTracker.Record("add.i4.MaxPlusOne", unchecked(int.MaxValue + 1) == int.MinValue);
        TestTracker.Record("add.i4.MinMinusOne", unchecked(int.MinValue + (-1)) == int.MaxValue);
        TestTracker.Record("add.i4.MinPlusMin", unchecked(int.MinValue + int.MinValue) == 0);
        TestTracker.Record("add.i4.MaxPlusMax", unchecked(int.MaxValue + int.MaxValue) == -2);

        // int32 commutativity
        TestTracker.Record("add.i4.Commutative", Add32(5, 7) == Add32(7, 5));
        TestTracker.Record("add.i4.Associative", Add32(Add32(1, 2), 3) == Add32(1, Add32(2, 3)));

        // int32 identity
        TestTracker.Record("add.i4.IdentityLeft", Add32(0, 42) == 42);
        TestTracker.Record("add.i4.IdentityRight", Add32(42, 0) == 42);

        // int64 tests
        TestTracker.Record("add.i8.ZeroZero", Add64(0L, 0L) == 0L);
        TestTracker.Record("add.i8.OnePlusOne", Add64(1L, 1L) == 2L);
        TestTracker.Record("add.i8.Simple", Add64(1L, 2L) == 3L);
        TestTracker.Record("add.i8.Large", Add64(0x100000000L, 0x100000000L) == 0x200000000L);
        TestTracker.Record("add.i8.MaxPlusZero", Add64(long.MaxValue, 0L) == long.MaxValue);
        TestTracker.Record("add.i8.MinPlusZero", Add64(long.MinValue, 0L) == long.MinValue);
        TestTracker.Record("add.i8.MaxPlusOne", unchecked(long.MaxValue + 1L) == long.MinValue);
        TestTracker.Record("add.i8.MinMinusOne", unchecked(long.MinValue + (-1L)) == long.MaxValue);
        TestTracker.Record("add.i8.Commutative", Add64(100L, 200L) == Add64(200L, 100L));

        // float tests
        TestTracker.Record("add.r4.Simple", Assert.AreApproxEqual(3.0f, Add32f(1.0f, 2.0f)));
        TestTracker.Record("add.r4.ZeroZero", Assert.AreApproxEqual(0.0f, Add32f(0.0f, 0.0f)));
        TestTracker.Record("add.r4.Negative", Assert.AreApproxEqual(-1.0f, Add32f(-3.0f, 2.0f)));
        TestTracker.Record("add.r4.Infinity", Assert.IsPositiveInfinity(Add32f(float.PositiveInfinity, 1.0f)));
        TestTracker.Record("add.r4.NegInfinity", Assert.IsNegativeInfinity(Add32f(float.NegativeInfinity, -1.0f)));
        TestTracker.Record("add.r4.InfPlusNegInf", Assert.IsNaN(Add32f(float.PositiveInfinity, float.NegativeInfinity)));
        TestTracker.Record("add.r4.NaN", Assert.IsNaN(Add32f(float.NaN, 1.0f)));
        TestTracker.Record("add.r4.NaNRight", Assert.IsNaN(Add32f(1.0f, float.NaN)));
        TestTracker.Record("add.r4.MaxPlusMax", Assert.IsPositiveInfinity(Add32f(float.MaxValue, float.MaxValue)));

        // double tests
        TestTracker.Record("add.r8.Simple", Assert.AreApproxEqual(3.0, Add64f(1.0, 2.0)));
        TestTracker.Record("add.r8.ZeroZero", Assert.AreApproxEqual(0.0, Add64f(0.0, 0.0)));
        TestTracker.Record("add.r8.Negative", Assert.AreApproxEqual(-1.0, Add64f(-3.0, 2.0)));
        TestTracker.Record("add.r8.Infinity", Assert.IsPositiveInfinity(Add64f(double.PositiveInfinity, 1.0)));
        TestTracker.Record("add.r8.NaN", Assert.IsNaN(Add64f(double.NaN, 1.0)));
        TestTracker.Record("add.r8.Commutative", Assert.AreApproxEqual(Add64f(1.5, 2.5), Add64f(2.5, 1.5)));
    }

    private static int Add32(int a, int b) => a + b;
    private static long Add64(long a, long b) => a + b;
    private static float Add32f(float a, float b) => a + b;
    private static double Add64f(double a, double b) => a + b;

    #endregion

    #region sub (0x59)

    private static void TestSub()
    {
        // int32 tests - basic
        TestTracker.Record("sub.i4.ZeroZero", Sub32(0, 0) == 0);
        TestTracker.Record("sub.i4.Simple", Sub32(5, 3) == 2);
        TestTracker.Record("sub.i4.Negative", Sub32(3, 5) == -2);
        TestTracker.Record("sub.i4.Zero", Sub32(5, 5) == 0);
        TestTracker.Record("sub.i4.NegFromNeg", Sub32(-1, -1) == 0);
        TestTracker.Record("sub.i4.FromZero", Sub32(0, 5) == -5);
        TestTracker.Record("sub.i4.Identity", Sub32(42, 0) == 42);

        // int32 overflow wrapping
        TestTracker.Record("sub.i4.MinMinusOne", unchecked(int.MinValue - 1) == int.MaxValue);
        TestTracker.Record("sub.i4.MaxMinusNegOne", unchecked(int.MaxValue - (-1)) == int.MinValue);
        TestTracker.Record("sub.i4.ZeroMinusMin", unchecked(0 - int.MinValue) == int.MinValue);
        TestTracker.Record("sub.i4.MinMinusMax", unchecked(int.MinValue - int.MaxValue) == 1);

        // int32 anti-commutativity: a - b = -(b - a)
        TestTracker.Record("sub.i4.AntiCommutative", Sub32(5, 3) == -Sub32(3, 5));

        // int32 subtraction as negative addition: a - b = a + (-b)
        TestTracker.Record("sub.i4.AsNegAdd", Sub32(10, 3) == Add32(10, -3));

        // int64 tests
        TestTracker.Record("sub.i8.ZeroZero", Sub64(0L, 0L) == 0L);
        TestTracker.Record("sub.i8.Simple", Sub64(100L, 30L) == 70L);
        TestTracker.Record("sub.i8.Large", Sub64(0x200000000L, 0x100000000L) == 0x100000000L);
        TestTracker.Record("sub.i8.MinMinusOne", unchecked(long.MinValue - 1L) == long.MaxValue);
        TestTracker.Record("sub.i8.AntiCommutative", Sub64(100L, 30L) == -Sub64(30L, 100L));

        // float tests
        TestTracker.Record("sub.r4.Simple", Assert.AreApproxEqual(2.0f, Sub32f(5.0f, 3.0f)));
        TestTracker.Record("sub.r4.Negative", Assert.AreApproxEqual(-2.0f, Sub32f(3.0f, 5.0f)));
        TestTracker.Record("sub.r4.Zero", Assert.AreApproxEqual(0.0f, Sub32f(5.0f, 5.0f)));
        TestTracker.Record("sub.r4.Infinity", Assert.IsPositiveInfinity(Sub32f(float.PositiveInfinity, 1.0f)));
        TestTracker.Record("sub.r4.InfMinusInf", Assert.IsNaN(Sub32f(float.PositiveInfinity, float.PositiveInfinity)));
        TestTracker.Record("sub.r4.NaN", Assert.IsNaN(Sub32f(float.NaN, 1.0f)));

        // double tests
        TestTracker.Record("sub.r8.Simple", Assert.AreApproxEqual(2.0, Sub64f(5.0, 3.0)));
        TestTracker.Record("sub.r8.Negative", Assert.AreApproxEqual(-2.0, Sub64f(3.0, 5.0)));
        TestTracker.Record("sub.r8.Infinity", Assert.IsPositiveInfinity(Sub64f(double.PositiveInfinity, 1.0)));
        TestTracker.Record("sub.r8.NaN", Assert.IsNaN(Sub64f(double.NaN, 1.0)));
    }

    private static int Sub32(int a, int b) => a - b;
    private static long Sub64(long a, long b) => a - b;
    private static float Sub32f(float a, float b) => a - b;
    private static double Sub64f(double a, double b) => a - b;

    #endregion

    #region mul (0x5A)

    private static void TestMul()
    {
        // int32 tests - basic
        TestTracker.Record("mul.i4.Simple", Mul32(3, 4) == 12);
        TestTracker.Record("mul.i4.Zero", Mul32(1000, 0) == 0);
        TestTracker.Record("mul.i4.ZeroLeft", Mul32(0, 1000) == 0);
        TestTracker.Record("mul.i4.Identity", Mul32(42, 1) == 42);
        TestTracker.Record("mul.i4.IdentityLeft", Mul32(1, 42) == 42);
        TestTracker.Record("mul.i4.Negative", Mul32(-3, 4) == -12);
        TestTracker.Record("mul.i4.NegativeRight", Mul32(3, -4) == -12);
        TestTracker.Record("mul.i4.BothNegative", Mul32(-3, -4) == 12);
        TestTracker.Record("mul.i4.Negation", Mul32(-1, 42) == -42);

        // int32 overflow wrapping
        TestTracker.Record("mul.i4.MaxTimes2", unchecked(int.MaxValue * 2) == -2);
        TestTracker.Record("mul.i4.MinTimesNeg1", unchecked(int.MinValue * -1) == int.MinValue);

        // int32 commutativity/associativity
        TestTracker.Record("mul.i4.Commutative", Mul32(5, 7) == Mul32(7, 5));
        TestTracker.Record("mul.i4.Associative", Mul32(Mul32(2, 3), 4) == Mul32(2, Mul32(3, 4)));

        // int32 distributive: a * (b + c) = a*b + a*c
        TestTracker.Record("mul.i4.Distributive", Mul32(3, Add32(4, 5)) == Add32(Mul32(3, 4), Mul32(3, 5)));

        // Powers of 2 (equivalent to shift)
        TestTracker.Record("mul.i4.Times2", Mul32(5, 2) == (5 << 1));
        TestTracker.Record("mul.i4.Times4", Mul32(5, 4) == (5 << 2));
        TestTracker.Record("mul.i4.Times8", Mul32(5, 8) == (5 << 3));

        // int64 tests
        TestTracker.Record("mul.i8.Simple", Mul64(1000000L, 1000000L) == 1000000000000L);
        TestTracker.Record("mul.i8.Zero", Mul64(0L, 123456789L) == 0L);
        TestTracker.Record("mul.i8.Identity", Mul64(1L, 123456789L) == 123456789L);
        TestTracker.Record("mul.i8.BeyondInt32", Mul64((long)int.MaxValue, (long)int.MaxValue) == 4611686014132420609L);
        TestTracker.Record("mul.i8.Commutative", Mul64(100L, 200L) == Mul64(200L, 100L));

        // float tests
        TestTracker.Record("mul.r4.Simple", Assert.AreApproxEqual(6.0f, Mul32f(2.0f, 3.0f)));
        TestTracker.Record("mul.r4.Zero", Assert.AreApproxEqual(0.0f, Mul32f(0.0f, 100.0f)));
        TestTracker.Record("mul.r4.Negative", Assert.AreApproxEqual(-6.0f, Mul32f(-2.0f, 3.0f)));
        TestTracker.Record("mul.r4.BothNegative", Assert.AreApproxEqual(6.0f, Mul32f(-2.0f, -3.0f)));
        TestTracker.Record("mul.r4.ZeroTimesInf", Assert.IsNaN(Mul32f(0.0f, float.PositiveInfinity)));
        TestTracker.Record("mul.r4.InfTimes2", Assert.IsPositiveInfinity(Mul32f(float.PositiveInfinity, 2.0f)));
        TestTracker.Record("mul.r4.InfTimesNeg", Assert.IsNegativeInfinity(Mul32f(float.PositiveInfinity, -2.0f)));
        TestTracker.Record("mul.r4.NaN", Assert.IsNaN(Mul32f(float.NaN, 1.0f)));
        TestTracker.Record("mul.r4.VerySmall", Mul32f(float.Epsilon, float.Epsilon) == 0.0f);
        TestTracker.Record("mul.r4.VeryLarge", Assert.IsPositiveInfinity(Mul32f(float.MaxValue, float.MaxValue)));

        // double tests
        TestTracker.Record("mul.r8.Simple", Assert.AreApproxEqual(6.0, Mul64f(2.0, 3.0)));
        TestTracker.Record("mul.r8.Zero", Assert.AreApproxEqual(0.0, Mul64f(0.0, 100.0)));
        TestTracker.Record("mul.r8.Negative", Assert.AreApproxEqual(-6.0, Mul64f(-2.0, 3.0)));
        TestTracker.Record("mul.r8.NaN", Assert.IsNaN(Mul64f(double.NaN, 1.0)));
        TestTracker.Record("mul.r8.Commutative", Assert.AreApproxEqual(Mul64f(2.5, 3.5), Mul64f(3.5, 2.5)));
    }

    private static int Mul32(int a, int b) => a * b;
    private static long Mul64(long a, long b) => a * b;
    private static float Mul32f(float a, float b) => a * b;
    private static double Mul64f(double a, double b) => a * b;

    #endregion

    #region div (0x5B)

    private static void TestDiv()
    {
        // int32 tests - basic
        TestTracker.Record("div.i4.Exact", Div32(10, 2) == 5);
        TestTracker.Record("div.i4.Truncate", Div32(10, 3) == 3);
        TestTracker.Record("div.i4.TruncateNeg", Div32(-10, 3) == -3);
        TestTracker.Record("div.i4.TruncateNegDiv", Div32(10, -3) == -3);
        TestTracker.Record("div.i4.BothNeg", Div32(-10, -3) == 3);
        TestTracker.Record("div.i4.ZeroDividend", Div32(0, 5) == 0);
        TestTracker.Record("div.i4.Identity", Div32(42, 1) == 42);
        TestTracker.Record("div.i4.Negation", Div32(42, -1) == -42);

        // Truncation direction tests
        TestTracker.Record("div.i4.Trunc5Div3", Div32(5, 3) == 1);
        TestTracker.Record("div.i4.TruncNeg5Div3", Div32(-5, 3) == -1);  // Toward zero, not -2
        TestTracker.Record("div.i4.Trunc5DivNeg3", Div32(5, -3) == -1);  // Toward zero
        TestTracker.Record("div.i4.TruncNeg5DivNeg3", Div32(-5, -3) == 1);

        // int32 MinValue / -1 is overflow (result doesn't fit)
        TestTracker.Record("div.i4.MinDivNeg1", unchecked(int.MinValue / -1) == int.MinValue);

        // int64 tests
        TestTracker.Record("div.i8.Exact", Div64(100L, 10L) == 10L);
        TestTracker.Record("div.i8.Truncate", Div64(100L, 30L) == 3L);
        TestTracker.Record("div.i8.Large", Div64(0x200000000L, 2L) == 0x100000000L);
        TestTracker.Record("div.i8.ZeroDividend", Div64(0L, 100L) == 0L);
        TestTracker.Record("div.i8.MinDivNeg1", unchecked(long.MinValue / -1L) == long.MinValue);

        // float tests - no exception on divide by zero
        TestTracker.Record("div.r4.Exact", Assert.AreApproxEqual(2.5f, Div32f(5.0f, 2.0f)));
        TestTracker.Record("div.r4.Repeating", Assert.AreApproxEqual(0.333333f, Div32f(1.0f, 3.0f), 0.0001f));
        TestTracker.Record("div.r4.ByZeroPos", Assert.IsPositiveInfinity(Div32f(1.0f, 0.0f)));
        TestTracker.Record("div.r4.ByZeroNeg", Assert.IsNegativeInfinity(Div32f(-1.0f, 0.0f)));
        TestTracker.Record("div.r4.ZeroByZero", Assert.IsNaN(Div32f(0.0f, 0.0f)));
        TestTracker.Record("div.r4.InfByInf", Assert.IsNaN(Div32f(float.PositiveInfinity, float.PositiveInfinity)));
        TestTracker.Record("div.r4.XByInf", Assert.AreApproxEqual(0.0f, Div32f(1.0f, float.PositiveInfinity)));
        TestTracker.Record("div.r4.InfByX", Assert.IsPositiveInfinity(Div32f(float.PositiveInfinity, 1.0f)));
        TestTracker.Record("div.r4.NaN", Assert.IsNaN(Div32f(float.NaN, 1.0f)));

        // double tests
        TestTracker.Record("div.r8.Exact", Assert.AreApproxEqual(2.5, Div64f(5.0, 2.0)));
        TestTracker.Record("div.r8.Repeating", Assert.AreApproxEqual(0.333333, Div64f(1.0, 3.0), 0.0001));
        TestTracker.Record("div.r8.ByZeroPos", Assert.IsPositiveInfinity(Div64f(1.0, 0.0)));
        TestTracker.Record("div.r8.ByZeroNeg", Assert.IsNegativeInfinity(Div64f(-1.0, 0.0)));
        TestTracker.Record("div.r8.ZeroByZero", Assert.IsNaN(Div64f(0.0, 0.0)));
        TestTracker.Record("div.r8.NaN", Assert.IsNaN(Div64f(double.NaN, 1.0)));
    }

    private static int Div32(int a, int b) => a / b;
    private static long Div64(long a, long b) => a / b;
    private static float Div32f(float a, float b) => a / b;
    private static double Div64f(double a, double b) => a / b;

    #endregion

    #region div.un (0x5C)

    private static void TestDivUn()
    {
        // div.un treats operands as unsigned
        TestTracker.Record("div.un.i4.Simple", DivUn32(6u, 2u) == 3u);
        TestTracker.Record("div.un.i4.Truncate", DivUn32(7u, 2u) == 3u);
        TestTracker.Record("div.un.i4.ZeroDividend", DivUn32(0u, 5u) == 0u);
        TestTracker.Record("div.un.i4.Identity", DivUn32(42u, 1u) == 42u);
        TestTracker.Record("div.un.i4.HighBit", DivUn32(0x80000000u, 2u) == 0x40000000u);
        TestTracker.Record("div.un.i4.MaxValue", DivUn32(0xFFFFFFFFu, 2u) == 0x7FFFFFFFu);
        TestTracker.Record("div.un.i4.MaxBy10", DivUn32(0xFFFFFFFFu, 10u) == 429496729u);

        // Interpretation difference from signed
        // -1 treated as 0xFFFFFFFF (unsigned max)
        TestTracker.Record("div.un.i4.Neg1AsMax", DivUn32(unchecked((uint)-1), 2u) == 0x7FFFFFFFu);
        // 0x80000000 is 2147483648 (not MinValue)
        TestTracker.Record("div.un.i4.HighBitNotNeg", DivUn32(0x80000000u, 1u) == 0x80000000u);

        // uint64 tests
        TestTracker.Record("div.un.i8.Simple", DivUn64(6UL, 2UL) == 3UL);
        TestTracker.Record("div.un.i8.HighBit", DivUn64(0x8000000000000000UL, 2UL) == 0x4000000000000000UL);
        TestTracker.Record("div.un.i8.MaxValue", DivUn64(0xFFFFFFFFFFFFFFFFUL, 2UL) == 0x7FFFFFFFFFFFFFFFUL);
        TestTracker.Record("div.un.i8.Identity", DivUn64(0xFFFFFFFFFFFFFFFFUL, 1UL) == 0xFFFFFFFFFFFFFFFFUL);
    }

    private static uint DivUn32(uint a, uint b) => a / b;
    private static ulong DivUn64(ulong a, ulong b) => a / b;

    #endregion

    #region rem (0x5D)

    private static void TestRem()
    {
        // int32 tests - basic
        TestTracker.Record("rem.i4.Basic", Rem32(7, 3) == 1);
        TestTracker.Record("rem.i4.Exact", Rem32(6, 3) == 0);
        TestTracker.Record("rem.i4.Negative", Rem32(-7, 3) == -1);  // Sign of dividend
        TestTracker.Record("rem.i4.NegDivisor", Rem32(7, -3) == 1);  // Sign of dividend
        TestTracker.Record("rem.i4.BothNeg", Rem32(-7, -3) == -1);  // Sign of dividend
        TestTracker.Record("rem.i4.ZeroDividend", Rem32(0, 5) == 0);
        TestTracker.Record("rem.i4.ModOne", Rem32(42, 1) == 0);
        TestTracker.Record("rem.i4.ModNegOne", Rem32(42, -1) == 0);
        TestTracker.Record("rem.i4.Large", Rem32(100, 30) == 10);

        // Mathematical identity: a = (a / b) * b + (a % b)
        int a = 17, b = 5;
        TestTracker.Record("rem.i4.Identity", a == (Div32(a, b) * b) + Rem32(a, b));

        // Sign rule: remainder has same sign as dividend
        TestTracker.Record("rem.i4.SignRule1", Rem32(7, 3) > 0);
        TestTracker.Record("rem.i4.SignRule2", Rem32(-7, 3) < 0);
        TestTracker.Record("rem.i4.SignRule3", Rem32(7, -3) > 0);
        TestTracker.Record("rem.i4.SignRule4", Rem32(-7, -3) < 0);

        // int64 tests
        TestTracker.Record("rem.i8.Basic", Rem64(100L, 30L) == 10L);
        TestTracker.Record("rem.i8.Large", Rem64(long.MaxValue, 10L) == 7L);
        TestTracker.Record("rem.i8.Negative", Rem64(-100L, 30L) == -10L);

        // float tests
        TestTracker.Record("rem.r4.Exact", Assert.AreApproxEqual(0.0f, Rem32f(7.5f, 2.5f)));
        TestTracker.Record("rem.r4.Basic", Assert.AreApproxEqual(1.0f, Rem32f(7.0f, 2.0f)));
        TestTracker.Record("rem.r4.Negative", Assert.AreApproxEqual(-1.0f, Rem32f(-7.0f, 2.0f)));
        TestTracker.Record("rem.r4.Half", Assert.AreApproxEqual(0.5f, Rem32f(5.5f, 2.5f)));

        // double tests
        TestTracker.Record("rem.r8.Exact", Assert.AreApproxEqual(0.0, Rem64f(7.5, 2.5)));
        TestTracker.Record("rem.r8.Basic", Assert.AreApproxEqual(1.5, Rem64f(5.5, 2.0)));
        TestTracker.Record("rem.r8.Negative", Assert.AreApproxEqual(-1.5, Rem64f(-5.5, 2.0)));
    }

    private static int Rem32(int a, int b) => a % b;
    private static long Rem64(long a, long b) => a % b;
    private static float Rem32f(float a, float b) => a % b;
    private static double Rem64f(double a, double b) => a % b;

    #endregion

    #region rem.un (0x5E)

    private static void TestRemUn()
    {
        // Unsigned remainder
        TestTracker.Record("rem.un.i4.Basic", RemUn32(7u, 3u) == 1u);
        TestTracker.Record("rem.un.i4.Exact", RemUn32(6u, 3u) == 0u);
        TestTracker.Record("rem.un.i4.ZeroDividend", RemUn32(0u, 5u) == 0u);
        TestTracker.Record("rem.un.i4.Large", RemUn32(0xFFFFFFFFu, 10u) == 5u);
        TestTracker.Record("rem.un.i4.HighBit", RemUn32(0x80000000u, 3u) == 2u);

        // Interpretation difference
        TestTracker.Record("rem.un.i4.Neg1AsMax", RemUn32(unchecked((uint)-1), 10u) == 5u);

        // Mathematical identity for unsigned
        uint au = 17u, bu = 5u;
        TestTracker.Record("rem.un.i4.Identity", au == (DivUn32(au, bu) * bu) + RemUn32(au, bu));

        // uint64 tests
        TestTracker.Record("rem.un.i8.Basic", RemUn64(7UL, 3UL) == 1UL);
        TestTracker.Record("rem.un.i8.Large", RemUn64(0xFFFFFFFFFFFFFFFFUL, 10UL) == 5UL);
        TestTracker.Record("rem.un.i8.HighBit", RemUn64(0x8000000000000000UL, 3UL) == 2UL);
    }

    private static uint RemUn32(uint a, uint b) => a % b;
    private static ulong RemUn64(ulong a, ulong b) => a % b;

    #endregion

    #region neg (0x65)

    private static void TestNeg()
    {
        // int32 negation
        TestTracker.Record("neg.i4.Positive", Negate32(42) == -42);
        TestTracker.Record("neg.i4.Negative", Negate32(-42) == 42);
        TestTracker.Record("neg.i4.Zero", Negate32(0) == 0);
        TestTracker.Record("neg.i4.One", Negate32(1) == -1);
        TestTracker.Record("neg.i4.NegOne", Negate32(-1) == 1);
        TestTracker.Record("neg.i4.MaxValue", Negate32(int.MaxValue) == -int.MaxValue);
        TestTracker.Record("neg.i4.MinValue", Negate32(int.MinValue) == int.MinValue); // Two's complement quirk

        // Double negation
        TestTracker.Record("neg.i4.DoubleNeg", Negate32(Negate32(42)) == 42);

        // int64 negation
        TestTracker.Record("neg.i8.Positive", Negate64(100L) == -100L);
        TestTracker.Record("neg.i8.Negative", Negate64(-100L) == 100L);
        TestTracker.Record("neg.i8.Zero", Negate64(0L) == 0L);
        TestTracker.Record("neg.i8.MaxValue", Negate64(long.MaxValue) == -long.MaxValue);
        TestTracker.Record("neg.i8.MinValue", Negate64(long.MinValue) == long.MinValue); // Two's complement

        // float negation
        TestTracker.Record("neg.r4.Positive", Assert.AreApproxEqual(-3.14f, Negate32f(3.14f)));
        TestTracker.Record("neg.r4.Negative", Assert.AreApproxEqual(3.14f, Negate32f(-3.14f)));
        TestTracker.Record("neg.r4.Zero", Assert.AreApproxEqual(0.0f, Negate32f(0.0f)));
        TestTracker.Record("neg.r4.Infinity", Assert.IsNegativeInfinity(Negate32f(float.PositiveInfinity)));
        TestTracker.Record("neg.r4.NegInfinity", Assert.IsPositiveInfinity(Negate32f(float.NegativeInfinity)));
        TestTracker.Record("neg.r4.NaN", Assert.IsNaN(Negate32f(float.NaN)));

        // double negation
        TestTracker.Record("neg.r8.Positive", Assert.AreApproxEqual(-3.14, Negate64f(3.14)));
        TestTracker.Record("neg.r8.Negative", Assert.AreApproxEqual(3.14, Negate64f(-3.14)));
        TestTracker.Record("neg.r8.Zero", Assert.AreApproxEqual(0.0, Negate64f(0.0)));
        TestTracker.Record("neg.r8.Infinity", Assert.IsNegativeInfinity(Negate64f(double.PositiveInfinity)));
        TestTracker.Record("neg.r8.NaN", Assert.IsNaN(Negate64f(double.NaN)));
    }

    private static int Negate32(int x) => -x;
    private static long Negate64(long x) => -x;
    private static float Negate32f(float x) => -x;
    private static double Negate64f(double x) => -x;

    #endregion

    #region add.ovf (0xD6)

    private static void TestAddOvf()
    {
        // add.ovf throws on overflow for signed
        // No overflow cases
        TestTracker.Record("add.ovf.i4.ZeroZero", AddOvf32(0, 0) == 0);
        TestTracker.Record("add.ovf.i4.Simple", AddOvf32(1, 1) == 2);
        TestTracker.Record("add.ovf.i4.Negative", AddOvf32(-1, 1) == 0);
        TestTracker.Record("add.ovf.i4.MaxPlusZero", AddOvf32(int.MaxValue, 0) == int.MaxValue);
        TestTracker.Record("add.ovf.i4.MinPlusZero", AddOvf32(int.MinValue, 0) == int.MinValue);
        TestTracker.Record("add.ovf.i4.LargeOk", AddOvf32(1000000, 1000000) == 2000000);

        // int64 no overflow
        TestTracker.Record("add.ovf.i8.Simple", AddOvf64(1L, 1L) == 2L);
        TestTracker.Record("add.ovf.i8.Large", AddOvf64(1000000000000L, 1000000000000L) == 2000000000000L);

        // Overflow cases would throw - we test they don't corrupt state
        // MaxValue + 1 would throw, MaxValue + 0 doesn't
        TestTracker.Record("add.ovf.i4.BoundaryOk", AddOvf32(int.MaxValue - 1, 1) == int.MaxValue);
        TestTracker.Record("add.ovf.i8.BoundaryOk", AddOvf64(long.MaxValue - 1L, 1L) == long.MaxValue);
    }

    private static int AddOvf32(int a, int b) => checked(a + b);
    private static long AddOvf64(long a, long b) => checked(a + b);

    #endregion

    #region add.ovf.un (0xD7)

    private static void TestAddOvfUn()
    {
        // add.ovf.un throws on overflow for unsigned
        // No overflow cases
        TestTracker.Record("add.ovf.un.i4.ZeroZero", AddOvfUn32(0u, 0u) == 0u);
        TestTracker.Record("add.ovf.un.i4.Simple", AddOvfUn32(1u, 1u) == 2u);
        TestTracker.Record("add.ovf.un.i4.Large", AddOvfUn32(1000000u, 1000000u) == 2000000u);
        TestTracker.Record("add.ovf.un.i4.JustUnderMax", AddOvfUn32(0xFFFFFFFEu, 1u) == 0xFFFFFFFFu);
        TestTracker.Record("add.ovf.un.i4.ZeroPlusMax", AddOvfUn32(0u, 0xFFFFFFFFu) == 0xFFFFFFFFu);

        // uint64 no overflow
        TestTracker.Record("add.ovf.un.i8.Simple", AddOvfUn64(1UL, 1UL) == 2UL);
        TestTracker.Record("add.ovf.un.i8.Large", AddOvfUn64(0x100000000UL, 0x100000000UL) == 0x200000000UL);
        TestTracker.Record("add.ovf.un.i8.JustUnderMax", AddOvfUn64(0xFFFFFFFFFFFFFFFEUL, 1UL) == 0xFFFFFFFFFFFFFFFFUL);
    }

    private static uint AddOvfUn32(uint a, uint b) => checked(a + b);
    private static ulong AddOvfUn64(ulong a, ulong b) => checked(a + b);

    #endregion

    #region sub.ovf (0xDA)

    private static void TestSubOvf()
    {
        // sub.ovf throws on overflow for signed
        // No overflow cases
        TestTracker.Record("sub.ovf.i4.ZeroZero", SubOvf32(0, 0) == 0);
        TestTracker.Record("sub.ovf.i4.Simple", SubOvf32(5, 3) == 2);
        TestTracker.Record("sub.ovf.i4.Negative", SubOvf32(3, 5) == -2);
        TestTracker.Record("sub.ovf.i4.MinMinusZero", SubOvf32(int.MinValue, 0) == int.MinValue);
        TestTracker.Record("sub.ovf.i4.MaxMinusZero", SubOvf32(int.MaxValue, 0) == int.MaxValue);

        // Boundary tests that don't overflow
        TestTracker.Record("sub.ovf.i4.MaxMinusMax", SubOvf32(int.MaxValue, int.MaxValue) == 0);
        TestTracker.Record("sub.ovf.i4.MinMinusMin", SubOvf32(int.MinValue, int.MinValue) == 0);

        // int64 no overflow
        TestTracker.Record("sub.ovf.i8.Simple", SubOvf64(100L, 30L) == 70L);
        TestTracker.Record("sub.ovf.i8.Large", SubOvf64(1000000000000L, 500000000000L) == 500000000000L);
    }

    private static int SubOvf32(int a, int b) => checked(a - b);
    private static long SubOvf64(long a, long b) => checked(a - b);

    #endregion

    #region sub.ovf.un (0xDB)

    private static void TestSubOvfUn()
    {
        // sub.ovf.un throws on overflow for unsigned
        // No overflow cases (b <= a)
        TestTracker.Record("sub.ovf.un.i4.ZeroZero", SubOvfUn32(0u, 0u) == 0u);
        TestTracker.Record("sub.ovf.un.i4.Simple", SubOvfUn32(5u, 3u) == 2u);
        TestTracker.Record("sub.ovf.un.i4.MaxMinusOne", SubOvfUn32(0xFFFFFFFFu, 1u) == 0xFFFFFFFEu);
        TestTracker.Record("sub.ovf.un.i4.MaxMinusMax", SubOvfUn32(0xFFFFFFFFu, 0xFFFFFFFFu) == 0u);
        TestTracker.Record("sub.ovf.un.i4.HighBit", SubOvfUn32(0x80000000u, 0x40000000u) == 0x40000000u);

        // uint64 no overflow
        TestTracker.Record("sub.ovf.un.i8.Simple", SubOvfUn64(100UL, 30UL) == 70UL);
        TestTracker.Record("sub.ovf.un.i8.Large", SubOvfUn64(0xFFFFFFFFFFFFFFFFUL, 1UL) == 0xFFFFFFFFFFFFFFFEUL);
    }

    private static uint SubOvfUn32(uint a, uint b) => checked(a - b);
    private static ulong SubOvfUn64(ulong a, ulong b) => checked(a - b);

    #endregion

    #region mul.ovf (0xD8)

    private static void TestMulOvf()
    {
        // mul.ovf throws on overflow for signed
        // No overflow cases
        TestTracker.Record("mul.ovf.i4.ZeroTimesAny", MulOvf32(0, int.MaxValue) == 0);
        TestTracker.Record("mul.ovf.i4.Identity", MulOvf32(1, 42) == 42);
        TestTracker.Record("mul.ovf.i4.Simple", MulOvf32(100, 100) == 10000);
        TestTracker.Record("mul.ovf.i4.Negative", MulOvf32(-3, 4) == -12);
        TestTracker.Record("mul.ovf.i4.BothNeg", MulOvf32(-3, -4) == 12);
        TestTracker.Record("mul.ovf.i4.Negation", MulOvf32(-1, 42) == -42);

        // Largest multiplication that doesn't overflow
        // sqrt(MaxValue) ≈ 46340
        TestTracker.Record("mul.ovf.i4.LargeOk", MulOvf32(46340, 46340) == 2147395600);

        // int64 no overflow
        TestTracker.Record("mul.ovf.i8.Simple", MulOvf64(1000000L, 1000000L) == 1000000000000L);
        TestTracker.Record("mul.ovf.i8.Identity", MulOvf64(1L, 123456789L) == 123456789L);
    }

    private static int MulOvf32(int a, int b) => checked(a * b);
    private static long MulOvf64(long a, long b) => checked(a * b);

    #endregion

    #region mul.ovf.un (0xD9)

    private static void TestMulOvfUn()
    {
        // mul.ovf.un throws on overflow for unsigned
        // No overflow cases
        TestTracker.Record("mul.ovf.un.i4.ZeroTimesAny", MulOvfUn32(0u, 0xFFFFFFFFu) == 0u);
        TestTracker.Record("mul.ovf.un.i4.Identity", MulOvfUn32(1u, 42u) == 42u);
        TestTracker.Record("mul.ovf.un.i4.Simple", MulOvfUn32(100u, 100u) == 10000u);

        // Largest multiplication that doesn't overflow
        // sqrt(0xFFFFFFFF) ≈ 65535
        TestTracker.Record("mul.ovf.un.i4.LargeOk", MulOvfUn32(65535u, 65535u) == 4294836225u);

        // uint64 no overflow
        TestTracker.Record("mul.ovf.un.i8.Simple", MulOvfUn64(1000000UL, 1000000UL) == 1000000000000UL);
        TestTracker.Record("mul.ovf.un.i8.Identity", MulOvfUn64(1UL, 0xFFFFFFFFFFFFFFFFUL) == 0xFFFFFFFFFFFFFFFFUL);
    }

    private static uint MulOvfUn32(uint a, uint b) => checked(a * b);
    private static ulong MulOvfUn64(ulong a, ulong b) => checked(a * b);

    #endregion

    #region Small Type Arithmetic Tests

    public static void RunSmallTypeTests()
    {
        TestAddSmallTypes();
        TestSubSmallTypes();
        TestMulSmallTypes();
        TestDivSmallTypes();
        TestRemSmallTypes();
        TestNegSmallTypes();
    }

    private static void TestAddSmallTypes()
    {
        // Byte arithmetic (zero-extends to int32)
        TestTracker.Record("add.byte.Basic", AddByte(10, 20) == 30);
        TestTracker.Record("add.byte.Zero", AddByte(100, 0) == 100);
        TestTracker.Record("add.byte.Max", AddByte(255, 0) == 255);
        TestTracker.Record("add.byte.Overflow", AddByte(200, 100) == 44); // Wraps in byte

        // SByte arithmetic (sign-extends to int32)
        TestTracker.Record("add.sbyte.Basic", AddSByte(10, 20) == 30);
        TestTracker.Record("add.sbyte.Negative", AddSByte(-10, 5) == -5);
        TestTracker.Record("add.sbyte.NegNeg", AddSByte(-10, -20) == -30);
        TestTracker.Record("add.sbyte.Zero", AddSByte(-50, 50) == 0);
        TestTracker.Record("add.sbyte.Max", AddSByte(127, 0) == 127);
        TestTracker.Record("add.sbyte.Min", AddSByte(-128, 0) == -128);

        // Short arithmetic (sign-extends to int32)
        TestTracker.Record("add.short.Basic", AddShort(1000, 2000) == 3000);
        TestTracker.Record("add.short.Negative", AddShort(-1000, 500) == -500);
        TestTracker.Record("add.short.NegNeg", AddShort(-1000, -2000) == -3000);
        TestTracker.Record("add.short.Zero", AddShort(-5000, 5000) == 0);
        TestTracker.Record("add.short.Max", AddShort(32767, 0) == 32767);
        TestTracker.Record("add.short.Min", AddShort(-32768, 0) == -32768);

        // UShort arithmetic (zero-extends to int32)
        TestTracker.Record("add.ushort.Basic", AddUShort(1000, 2000) == 3000);
        TestTracker.Record("add.ushort.Zero", AddUShort(10000, 0) == 10000);
        TestTracker.Record("add.ushort.Max", AddUShort(65535, 0) == 65535);
        TestTracker.Record("add.ushort.Overflow", AddUShort(60000, 10000) == 4464); // Wraps in ushort
    }

    private static byte AddByte(byte a, byte b) => (byte)(a + b);
    private static sbyte AddSByte(sbyte a, sbyte b) => (sbyte)(a + b);
    private static short AddShort(short a, short b) => (short)(a + b);
    private static ushort AddUShort(ushort a, ushort b) => (ushort)(a + b);

    private static void TestSubSmallTypes()
    {
        // Byte subtraction
        TestTracker.Record("sub.byte.Basic", SubByte(30, 10) == 20);
        TestTracker.Record("sub.byte.Zero", SubByte(100, 0) == 100);
        TestTracker.Record("sub.byte.ToZero", SubByte(50, 50) == 0);
        TestTracker.Record("sub.byte.Underflow", SubByte(10, 20) == 246); // Wraps

        // SByte subtraction
        TestTracker.Record("sub.sbyte.Basic", SubSByte(30, 10) == 20);
        TestTracker.Record("sub.sbyte.Negative", SubSByte(10, 30) == -20);
        TestTracker.Record("sub.sbyte.NegFromNeg", SubSByte(-10, -30) == 20);
        TestTracker.Record("sub.sbyte.ToZero", SubSByte(-50, -50) == 0);

        // Short subtraction
        TestTracker.Record("sub.short.Basic", SubShort(3000, 1000) == 2000);
        TestTracker.Record("sub.short.Negative", SubShort(1000, 3000) == -2000);
        TestTracker.Record("sub.short.NegFromNeg", SubShort(-1000, -3000) == 2000);
        TestTracker.Record("sub.short.ToZero", SubShort(-5000, -5000) == 0);

        // UShort subtraction
        TestTracker.Record("sub.ushort.Basic", SubUShort(3000, 1000) == 2000);
        TestTracker.Record("sub.ushort.Zero", SubUShort(10000, 0) == 10000);
        TestTracker.Record("sub.ushort.ToZero", SubUShort(50000, 50000) == 0);
        TestTracker.Record("sub.ushort.Underflow", SubUShort(1000, 2000) == 64536); // Wraps
    }

    private static byte SubByte(byte a, byte b) => (byte)(a - b);
    private static sbyte SubSByte(sbyte a, sbyte b) => (sbyte)(a - b);
    private static short SubShort(short a, short b) => (short)(a - b);
    private static ushort SubUShort(ushort a, ushort b) => (ushort)(a - b);

    private static void TestMulSmallTypes()
    {
        // Byte multiplication
        TestTracker.Record("mul.byte.Basic", MulByte(5, 6) == 30);
        TestTracker.Record("mul.byte.Zero", MulByte(100, 0) == 0);
        TestTracker.Record("mul.byte.Identity", MulByte(42, 1) == 42);
        TestTracker.Record("mul.byte.Overflow", MulByte(20, 20) == 144); // 400 wraps to 144

        // SByte multiplication
        TestTracker.Record("mul.sbyte.Basic", MulSByte(5, 6) == 30);
        TestTracker.Record("mul.sbyte.Negative", MulSByte(-5, 6) == -30);
        TestTracker.Record("mul.sbyte.NegNeg", MulSByte(-5, -6) == 30);
        TestTracker.Record("mul.sbyte.Zero", MulSByte(-100, 0) == 0);

        // Short multiplication
        TestTracker.Record("mul.short.Basic", MulShort(100, 200) == 20000);
        TestTracker.Record("mul.short.Negative", MulShort(-100, 200) == -20000);
        TestTracker.Record("mul.short.NegNeg", MulShort(-100, -200) == 20000);
        TestTracker.Record("mul.short.Zero", MulShort(-1000, 0) == 0);

        // UShort multiplication
        TestTracker.Record("mul.ushort.Basic", MulUShort(100, 200) == 20000);
        TestTracker.Record("mul.ushort.Zero", MulUShort(10000, 0) == 0);
        TestTracker.Record("mul.ushort.Identity", MulUShort(42, 1) == 42);
        TestTracker.Record("mul.ushort.Overflow", MulUShort(300, 300) == 24464); // 90000 wraps
    }

    private static byte MulByte(byte a, byte b) => (byte)(a * b);
    private static sbyte MulSByte(sbyte a, sbyte b) => (sbyte)(a * b);
    private static short MulShort(short a, short b) => (short)(a * b);
    private static ushort MulUShort(ushort a, ushort b) => (ushort)(a * b);

    private static void TestDivSmallTypes()
    {
        // Byte division
        TestTracker.Record("div.byte.Exact", DivByte(30, 5) == 6);
        TestTracker.Record("div.byte.Truncate", DivByte(30, 7) == 4);
        TestTracker.Record("div.byte.Identity", DivByte(42, 1) == 42);
        TestTracker.Record("div.byte.ZeroDiv", DivByte(0, 5) == 0);

        // SByte division
        TestTracker.Record("div.sbyte.Exact", DivSByte(30, 5) == 6);
        TestTracker.Record("div.sbyte.Negative", DivSByte(-30, 5) == -6);
        TestTracker.Record("div.sbyte.NegNeg", DivSByte(-30, -5) == 6);
        TestTracker.Record("div.sbyte.Truncate", DivSByte(30, 7) == 4);

        // Short division
        TestTracker.Record("div.short.Exact", DivShort(3000, 50) == 60);
        TestTracker.Record("div.short.Negative", DivShort(-3000, 50) == -60);
        TestTracker.Record("div.short.NegNeg", DivShort(-3000, -50) == 60);
        TestTracker.Record("div.short.Truncate", DivShort(3000, 70) == 42);

        // UShort division
        TestTracker.Record("div.ushort.Exact", DivUShort(3000, 50) == 60);
        TestTracker.Record("div.ushort.Truncate", DivUShort(3000, 70) == 42);
        TestTracker.Record("div.ushort.Identity", DivUShort(42, 1) == 42);
        TestTracker.Record("div.ushort.Large", DivUShort(60000, 2) == 30000);
    }

    private static byte DivByte(byte a, byte b) => (byte)(a / b);
    private static sbyte DivSByte(sbyte a, sbyte b) => (sbyte)(a / b);
    private static short DivShort(short a, short b) => (short)(a / b);
    private static ushort DivUShort(ushort a, ushort b) => (ushort)(a / b);

    private static void TestRemSmallTypes()
    {
        // Byte remainder
        TestTracker.Record("rem.byte.Basic", RemByte(30, 7) == 2);
        TestTracker.Record("rem.byte.Exact", RemByte(30, 5) == 0);
        TestTracker.Record("rem.byte.Zero", RemByte(0, 5) == 0);
        TestTracker.Record("rem.byte.Mod1", RemByte(42, 1) == 0);

        // SByte remainder
        TestTracker.Record("rem.sbyte.Basic", RemSByte(30, 7) == 2);
        TestTracker.Record("rem.sbyte.Negative", RemSByte(-30, 7) == -2);
        TestTracker.Record("rem.sbyte.NegDiv", RemSByte(30, -7) == 2);
        TestTracker.Record("rem.sbyte.NegNeg", RemSByte(-30, -7) == -2);

        // Short remainder (3000 / 70 = 42 remainder 60)
        TestTracker.Record("rem.short.Basic", RemShort(3000, 70) == 60);
        TestTracker.Record("rem.short.Negative", RemShort(-3000, 70) == -60);
        TestTracker.Record("rem.short.NegDiv", RemShort(3000, -70) == 60);
        TestTracker.Record("rem.short.NegNeg", RemShort(-3000, -70) == -60);

        // UShort remainder (3000 / 70 = 42 remainder 60, 60000 / 7 = 8571 remainder 3)
        TestTracker.Record("rem.ushort.Basic", RemUShort(3000, 70) == 60);
        TestTracker.Record("rem.ushort.Exact", RemUShort(3000, 50) == 0);
        TestTracker.Record("rem.ushort.Zero", RemUShort(0, 50) == 0);
        TestTracker.Record("rem.ushort.Large", RemUShort(60000, 7) == 3);
    }

    private static byte RemByte(byte a, byte b) => (byte)(a % b);
    private static sbyte RemSByte(sbyte a, sbyte b) => (sbyte)(a % b);
    private static short RemShort(short a, short b) => (short)(a % b);
    private static ushort RemUShort(ushort a, ushort b) => (ushort)(a % b);

    private static void TestNegSmallTypes()
    {
        // SByte negation
        TestTracker.Record("neg.sbyte.Positive", NegSByte(42) == -42);
        TestTracker.Record("neg.sbyte.Negative", NegSByte(-42) == 42);
        TestTracker.Record("neg.sbyte.Zero", NegSByte(0) == 0);
        TestTracker.Record("neg.sbyte.One", NegSByte(1) == -1);
        TestTracker.Record("neg.sbyte.Max", NegSByte(127) == -127);
        TestTracker.Record("neg.sbyte.Min", NegSByte(-128) == -128); // Two's complement quirk

        // Short negation
        TestTracker.Record("neg.short.Positive", NegShort(1000) == -1000);
        TestTracker.Record("neg.short.Negative", NegShort(-1000) == 1000);
        TestTracker.Record("neg.short.Zero", NegShort(0) == 0);
        TestTracker.Record("neg.short.Max", NegShort(32767) == -32767);
        TestTracker.Record("neg.short.Min", NegShort(-32768) == -32768); // Two's complement quirk
    }

    private static sbyte NegSByte(sbyte x) => (sbyte)(-x);
    private static short NegShort(short x) => (short)(-x);

    #endregion
}
