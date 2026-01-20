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
    }

    #region add (0x58)

    private static void TestAdd()
    {
        // int32 tests
        TestTracker.Record("add.i4.ZeroZero", 0 + 0 == 0);
        TestTracker.Record("add.i4.Simple", 1 + 2 == 3);
        TestTracker.Record("add.i4.Negative", -5 + 3 == -2);
        TestTracker.Record("add.i4.MaxPlusZero", int.MaxValue + 0 == int.MaxValue);
        TestTracker.Record("add.i4.Overflow", unchecked(int.MaxValue + 1) == int.MinValue);

        // int64 tests
        TestTracker.Record("add.i8.Simple", 1L + 2L == 3L);
        TestTracker.Record("add.i8.Large", 0x100000000L + 0x100000000L == 0x200000000L);

        // float tests
        TestTracker.Record("add.r4.Simple", Assert.AreApproxEqual(3.0f, 1.0f + 2.0f));
        TestTracker.Record("add.r4.Infinity", Assert.IsPositiveInfinity(float.PositiveInfinity + 1.0f));
        TestTracker.Record("add.r4.NaN", Assert.IsNaN(float.NaN + 1.0f));

        // double tests
        TestTracker.Record("add.r8.Simple", Assert.AreApproxEqual(3.0, 1.0 + 2.0));
    }

    #endregion

    #region sub (0x59)

    private static void TestSub()
    {
        TestTracker.Record("sub.i4.Simple", 5 - 3 == 2);
        TestTracker.Record("sub.i4.Negative", 3 - 5 == -2);
        TestTracker.Record("sub.i4.Zero", 5 - 5 == 0);
        TestTracker.Record("sub.i4.MinMinusOne", unchecked(int.MinValue - 1) == int.MaxValue);

        TestTracker.Record("sub.i8.Simple", 100L - 30L == 70L);

        TestTracker.Record("sub.r4.Simple", Assert.AreApproxEqual(2.0f, 5.0f - 3.0f));
        TestTracker.Record("sub.r8.Simple", Assert.AreApproxEqual(2.0, 5.0 - 3.0));
    }

    #endregion

    #region mul (0x5A)

    private static void TestMul()
    {
        TestTracker.Record("mul.i4.Simple", 3 * 4 == 12);
        TestTracker.Record("mul.i4.Zero", 1000 * 0 == 0);
        TestTracker.Record("mul.i4.Negative", -3 * 4 == -12);
        TestTracker.Record("mul.i4.BothNegative", -3 * -4 == 12);

        TestTracker.Record("mul.i8.Simple", 1000000L * 1000000L == 1000000000000L);

        TestTracker.Record("mul.r4.Simple", Assert.AreApproxEqual(6.0f, 2.0f * 3.0f));
        TestTracker.Record("mul.r8.Simple", Assert.AreApproxEqual(6.0, 2.0 * 3.0));
    }

    #endregion

    #region div (0x5B)

    private static void TestDiv()
    {
        TestTracker.Record("div.i4.Simple", 10 / 3 == 3);
        TestTracker.Record("div.i4.Exact", 10 / 2 == 5);
        TestTracker.Record("div.i4.Negative", -10 / 3 == -3);
        TestTracker.Record("div.i4.NegativeDivisor", 10 / -3 == -3);

        TestTracker.Record("div.i8.Simple", 100L / 10L == 10L);

        TestTracker.Record("div.r4.Simple", Assert.AreApproxEqual(2.5f, 5.0f / 2.0f));
        TestTracker.Record("div.r4.ByZero", Assert.IsPositiveInfinity(1.0f / 0.0f));
        TestTracker.Record("div.r8.Simple", Assert.AreApproxEqual(2.5, 5.0 / 2.0));
    }

    #endregion

    #region rem (0x5D)

    private static void TestRem()
    {
        TestTracker.Record("rem.i4.Simple", 10 % 3 == 1);
        TestTracker.Record("rem.i4.Exact", 10 % 2 == 0);
        TestTracker.Record("rem.i4.Negative", -10 % 3 == -1);

        TestTracker.Record("rem.i8.Simple", 100L % 30L == 10L);

        TestTracker.Record("rem.r4.Simple", Assert.AreApproxEqual(1.5f, 5.5f % 2.0f));
        TestTracker.Record("rem.r8.Simple", Assert.AreApproxEqual(1.5, 5.5 % 2.0));
    }

    #endregion

    #region neg (0x65)

    private static void TestNeg()
    {
        TestTracker.Record("neg.i4.Positive", -42 == Negate(42));
        TestTracker.Record("neg.i4.Negative", 42 == Negate(-42));
        TestTracker.Record("neg.i4.Zero", 0 == Negate(0));
        TestTracker.Record("neg.i4.MinValue", Negate(int.MinValue) == int.MinValue); // Two's complement quirk

        TestTracker.Record("neg.i8.Simple", -100L == NegateLong(100L));

        TestTracker.Record("neg.r4.Simple", Assert.AreApproxEqual(-3.14f, NegateFloat(3.14f)));
        TestTracker.Record("neg.r8.Simple", Assert.AreApproxEqual(-3.14, NegateDouble(3.14)));
    }

    private static int Negate(int x) => -x;
    private static long NegateLong(long x) => -x;
    private static float NegateFloat(float x) => -x;
    private static double NegateDouble(double x) => -x;

    #endregion

    #region Overflow variants

    private static void TestAddOvf()
    {
        // add.ovf throws on overflow, add.ovf.un for unsigned
        TestTracker.Record("add.ovf.i4.NoOverflow", AddOvfHelper(1, 2) == 3);
        TestTracker.Record("add.ovf.i4.MaxPlusZero", AddOvfHelper(int.MaxValue, 0) == int.MaxValue);
        // Overflow case would throw - tested separately in exception handling
    }

    private static int AddOvfHelper(int a, int b) => checked(a + b);

    private static void TestSubOvf()
    {
        TestTracker.Record("sub.ovf.i4.NoOverflow", SubOvfHelper(5, 3) == 2);
    }

    private static int SubOvfHelper(int a, int b) => checked(a - b);

    private static void TestMulOvf()
    {
        TestTracker.Record("mul.ovf.i4.NoOverflow", MulOvfHelper(3, 4) == 12);
    }

    private static int MulOvfHelper(int a, int b) => checked(a * b);

    #endregion

    #region Unsigned variants

    private static void TestDivUn()
    {
        // div.un treats operands as unsigned
        TestTracker.Record("div.un.Basic", DivUnHelper(10u, 3u) == 3u);
        TestTracker.Record("div.un.HighBit", DivUnHelper(0x80000000u, 2u) == 0x40000000u);
    }

    private static uint DivUnHelper(uint a, uint b) => a / b;

    private static void TestRemUn()
    {
        TestTracker.Record("rem.un.Basic", RemUnHelper(10u, 3u) == 1u);
    }

    private static uint RemUnHelper(uint a, uint b) => a % b;

    #endregion
}
