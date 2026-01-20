// JITTest - Comparison Instruction Tests
// Section 24: ceq, cgt, cgt.un, clt, clt.un

namespace JITTest;

/// <summary>
/// Tests for comparison instructions
/// </summary>
public static class ComparisonTests
{
    public static void RunAll()
    {
        TestCeq();
        TestCgt();
        TestCgtUn();
        TestClt();
        TestCltUn();
    }

    #region ceq (0xFE 0x01)

    private static void TestCeq()
    {
        // int32
        TestTracker.Record("ceq.i4.Equal", Ceq(5, 5));
        TestTracker.Record("ceq.i4.NotEqual", !Ceq(5, 10));
        TestTracker.Record("ceq.i4.Zero", Ceq(0, 0));
        TestTracker.Record("ceq.i4.Negative", Ceq(-5, -5));

        // int64
        TestTracker.Record("ceq.i8.Equal", CeqLong(100L, 100L));
        TestTracker.Record("ceq.i8.NotEqual", !CeqLong(100L, 200L));

        // float
        TestTracker.Record("ceq.r4.Equal", CeqFloat(1.0f, 1.0f));
        TestTracker.Record("ceq.r4.NaN", !CeqFloat(float.NaN, float.NaN)); // NaN != NaN

        // references
        string s = "test";
        TestTracker.Record("ceq.ref.Same", CeqRef(s, s));
        TestTracker.Record("ceq.ref.Null", CeqRef(null, null));
    }

    private static bool Ceq(int a, int b) => a == b;
    private static bool CeqLong(long a, long b) => a == b;
    private static bool CeqFloat(float a, float b) => a == b;
    private static bool CeqRef(object? a, object? b) => a == b;

    #endregion

    #region cgt (0xFE 0x02)

    private static void TestCgt()
    {
        TestTracker.Record("cgt.i4.Greater", Cgt(10, 5));
        TestTracker.Record("cgt.i4.NotGreater", !Cgt(5, 10));
        TestTracker.Record("cgt.i4.Equal", !Cgt(5, 5));
        TestTracker.Record("cgt.i4.Negative", Cgt(-5, -10));

        TestTracker.Record("cgt.r4.Greater", CgtFloat(2.0f, 1.0f));
        TestTracker.Record("cgt.r4.NaN", !CgtFloat(float.NaN, 1.0f)); // NaN comparisons are false
    }

    private static bool Cgt(int a, int b) => a > b;
    private static bool CgtFloat(float a, float b) => a > b;

    #endregion

    #region cgt.un (0xFE 0x03)

    private static void TestCgtUn()
    {
        TestTracker.Record("cgt.un.Basic", CgtUn(10u, 5u));
        TestTracker.Record("cgt.un.HighBit", CgtUn(0x80000000u, 0x7FFFFFFFu));

        // For floats, cgt.un returns true for unordered (NaN) comparisons
        TestTracker.Record("cgt.un.NaN", CgtUnFloat(float.NaN, 1.0f));
    }

    private static bool CgtUn(uint a, uint b) => a > b;
    private static bool CgtUnFloat(float a, float b) => !(a <= b); // Equivalent to cgt.un for floats

    #endregion

    #region clt (0xFE 0x04)

    private static void TestClt()
    {
        TestTracker.Record("clt.i4.Less", Clt(5, 10));
        TestTracker.Record("clt.i4.NotLess", !Clt(10, 5));
        TestTracker.Record("clt.i4.Equal", !Clt(5, 5));
        TestTracker.Record("clt.i4.Negative", Clt(-10, -5));

        TestTracker.Record("clt.r4.Less", CltFloat(1.0f, 2.0f));
        TestTracker.Record("clt.r4.NaN", !CltFloat(float.NaN, 1.0f));
    }

    private static bool Clt(int a, int b) => a < b;
    private static bool CltFloat(float a, float b) => a < b;

    #endregion

    #region clt.un (0xFE 0x05)

    private static void TestCltUn()
    {
        TestTracker.Record("clt.un.Basic", CltUn(5u, 10u));
        TestTracker.Record("clt.un.HighBit", CltUn(0x7FFFFFFFu, 0x80000000u));

        TestTracker.Record("clt.un.NaN", CltUnFloat(float.NaN, 1.0f));
    }

    private static bool CltUn(uint a, uint b) => a < b;
    private static bool CltUnFloat(float a, float b) => !(a >= b);

    #endregion
}
