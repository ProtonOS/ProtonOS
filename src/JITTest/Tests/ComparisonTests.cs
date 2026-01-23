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
        // int32 basic
        TestTracker.Record("ceq.i4.Equal", Ceq32(5, 5));
        TestTracker.Record("ceq.i4.NotEqual", !Ceq32(5, 10));
        TestTracker.Record("ceq.i4.Zero", Ceq32(0, 0));
        TestTracker.Record("ceq.i4.Negative", Ceq32(-5, -5));
        TestTracker.Record("ceq.i4.MaxValue", Ceq32(int.MaxValue, int.MaxValue));
        TestTracker.Record("ceq.i4.MinValue", Ceq32(int.MinValue, int.MinValue));
        TestTracker.Record("ceq.i4.MaxNotMin", !Ceq32(int.MaxValue, int.MinValue));
        TestTracker.Record("ceq.i4.NegNotPos", !Ceq32(-1, 1));
        TestTracker.Record("ceq.i4.NegOne", Ceq32(-1, -1));
        TestTracker.Record("ceq.i4.ZeroNotOne", !Ceq32(0, 1));
        TestTracker.Record("ceq.i4.OneNotNegOne", !Ceq32(1, -1));
        TestTracker.Record("ceq.i4.Large", Ceq32(1000000, 1000000));
        TestTracker.Record("ceq.i4.LargeNotSmall", !Ceq32(1000000, 1));
        TestTracker.Record("ceq.i4.Adjacent", !Ceq32(100, 101));
        TestTracker.Record("ceq.i4.AdjacentNeg", !Ceq32(-100, -101));

        // int32 signed interpretations
        TestTracker.Record("ceq.i4.SignedBit", Ceq32(unchecked((int)0x80000000), int.MinValue));

        // int64
        TestTracker.Record("ceq.i8.Equal", Ceq64(100L, 100L));
        TestTracker.Record("ceq.i8.NotEqual", !Ceq64(100L, 200L));
        TestTracker.Record("ceq.i8.Zero", Ceq64(0L, 0L));
        TestTracker.Record("ceq.i8.MaxValue", Ceq64(long.MaxValue, long.MaxValue));
        TestTracker.Record("ceq.i8.MinValue", Ceq64(long.MinValue, long.MinValue));
        TestTracker.Record("ceq.i8.LargePos", Ceq64(0x100000000L, 0x100000000L));
        TestTracker.Record("ceq.i8.LargeNotSmall", !Ceq64(0x100000000L, 0x1L));
        TestTracker.Record("ceq.i8.NegOne", Ceq64(-1L, -1L));

        // uint32 (ceq treats as bit patterns)
        TestTracker.Record("ceq.u4.Equal", CeqU32(5u, 5u));
        TestTracker.Record("ceq.u4.Max", CeqU32(uint.MaxValue, uint.MaxValue));
        TestTracker.Record("ceq.u4.HighBit", CeqU32(0x80000000u, 0x80000000u));

        // uint64
        TestTracker.Record("ceq.u8.Equal", CeqU64(100UL, 100UL));
        TestTracker.Record("ceq.u8.Max", CeqU64(ulong.MaxValue, ulong.MaxValue));
        TestTracker.Record("ceq.u8.Zero", CeqU64(0UL, 0UL));
        TestTracker.Record("ceq.u8.NotEqual", !CeqU64(100UL, 200UL));
        TestTracker.Record("ceq.u8.HighBit", CeqU64(0x8000000000000000UL, 0x8000000000000000UL));
        TestTracker.Record("ceq.u8.HighBitNotLow", !CeqU64(0x8000000000000000UL, 0x7FFFFFFFFFFFFFFFUL));

        // float32
        TestTracker.Record("ceq.r4.Equal", CeqFloat(1.0f, 1.0f));
        TestTracker.Record("ceq.r4.NotEqual", !CeqFloat(1.0f, 2.0f));
        TestTracker.Record("ceq.r4.Zero", CeqFloat(0.0f, 0.0f));
        TestTracker.Record("ceq.r4.NegZero", CeqFloat(0.0f, -0.0f)); // Positive and negative zero are equal
        TestTracker.Record("ceq.r4.NaN", !CeqFloat(float.NaN, float.NaN)); // NaN != NaN
        TestTracker.Record("ceq.r4.NaNLeft", !CeqFloat(float.NaN, 1.0f));
        TestTracker.Record("ceq.r4.NaNRight", !CeqFloat(1.0f, float.NaN));
        TestTracker.Record("ceq.r4.PosInf", CeqFloat(float.PositiveInfinity, float.PositiveInfinity));
        TestTracker.Record("ceq.r4.NegInf", CeqFloat(float.NegativeInfinity, float.NegativeInfinity));
        TestTracker.Record("ceq.r4.InfNotNegInf", !CeqFloat(float.PositiveInfinity, float.NegativeInfinity));

        // float64
        TestTracker.Record("ceq.r8.Equal", CeqDouble(1.0, 1.0));
        TestTracker.Record("ceq.r8.NotEqual", !CeqDouble(1.0, 2.0));
        TestTracker.Record("ceq.r8.NaN", !CeqDouble(double.NaN, double.NaN));
        TestTracker.Record("ceq.r8.PosInf", CeqDouble(double.PositiveInfinity, double.PositiveInfinity));
        TestTracker.Record("ceq.r8.NegZero", CeqDouble(0.0, -0.0));

        // references
        string s = "test";
        TestTracker.Record("ceq.ref.Same", CeqRef(s, s));
        TestTracker.Record("ceq.ref.Null", CeqRef(null, null));
        TestTracker.Record("ceq.ref.NullNotObj", !CeqRef(s, null));
        TestTracker.Record("ceq.ref.ObjNotNull", !CeqRef(null, s));

        // byte/int16 comparisons (widened to int32 on stack)
        TestTracker.Record("ceq.byte.Equal", CeqByte(100, 100));
        TestTracker.Record("ceq.byte.NotEqual", !CeqByte(100, 200));
        TestTracker.Record("ceq.byte.Zero", CeqByte(0, 0));
        TestTracker.Record("ceq.byte.Max", CeqByte(255, 255));
        TestTracker.Record("ceq.sbyte.Equal", CeqSByte(-50, -50));
        TestTracker.Record("ceq.sbyte.NotEqual", !CeqSByte(-50, 50));
        TestTracker.Record("ceq.sbyte.NegMax", CeqSByte(-128, -128));
        TestTracker.Record("ceq.short.Equal", CeqShort(1000, 1000));
        TestTracker.Record("ceq.short.NotEqual", !CeqShort(1000, -1000));
        TestTracker.Record("ceq.short.Max", CeqShort(short.MaxValue, short.MaxValue));
        TestTracker.Record("ceq.short.Min", CeqShort(short.MinValue, short.MinValue));
        TestTracker.Record("ceq.ushort.Equal", CeqUShort(50000, 50000));
        TestTracker.Record("ceq.ushort.Max", CeqUShort(ushort.MaxValue, ushort.MaxValue));
    }

    private static bool Ceq32(int a, int b) => a == b;
    private static bool Ceq64(long a, long b) => a == b;
    private static bool CeqU32(uint a, uint b) => a == b;
    private static bool CeqU64(ulong a, ulong b) => a == b;
    private static bool CeqFloat(float a, float b) => a == b;
    private static bool CeqDouble(double a, double b) => a == b;
    private static bool CeqRef(object? a, object? b) => a == b;
    private static bool CeqByte(byte a, byte b) => a == b;
    private static bool CeqSByte(sbyte a, sbyte b) => a == b;
    private static bool CeqShort(short a, short b) => a == b;
    private static bool CeqUShort(ushort a, ushort b) => a == b;

    #endregion

    #region cgt (0xFE 0x02)

    private static void TestCgt()
    {
        // int32 basic
        TestTracker.Record("cgt.i4.Greater", Cgt32(10, 5));
        TestTracker.Record("cgt.i4.NotGreater", !Cgt32(5, 10));
        TestTracker.Record("cgt.i4.Equal", !Cgt32(5, 5));
        TestTracker.Record("cgt.i4.Negative", Cgt32(-5, -10));
        TestTracker.Record("cgt.i4.ZeroNotNeg", Cgt32(0, -1));
        TestTracker.Record("cgt.i4.PosNotZero", Cgt32(1, 0));
        TestTracker.Record("cgt.i4.MaxNotMin", Cgt32(int.MaxValue, int.MinValue));
        TestTracker.Record("cgt.i4.MinNotMax", !Cgt32(int.MinValue, int.MaxValue));
        TestTracker.Record("cgt.i4.MaxPlusOne", !Cgt32(int.MaxValue - 1, int.MaxValue));
        TestTracker.Record("cgt.i4.NegOneNot", !Cgt32(-1, 0));

        // int64
        TestTracker.Record("cgt.i8.Greater", Cgt64(100L, 50L));
        TestTracker.Record("cgt.i8.NotGreater", !Cgt64(50L, 100L));
        TestTracker.Record("cgt.i8.Equal", !Cgt64(100L, 100L));
        TestTracker.Record("cgt.i8.Large", Cgt64(0x200000000L, 0x100000000L));
        TestTracker.Record("cgt.i8.NegNotPos", !Cgt64(-1L, 1L));

        // float32
        TestTracker.Record("cgt.r4.Greater", CgtFloat(2.0f, 1.0f));
        TestTracker.Record("cgt.r4.NotGreater", !CgtFloat(1.0f, 2.0f));
        TestTracker.Record("cgt.r4.Equal", !CgtFloat(1.0f, 1.0f));
        TestTracker.Record("cgt.r4.NaN", !CgtFloat(float.NaN, 1.0f)); // NaN comparisons are false
        TestTracker.Record("cgt.r4.NaNRight", !CgtFloat(1.0f, float.NaN));
        TestTracker.Record("cgt.r4.NaNBoth", !CgtFloat(float.NaN, float.NaN));
        TestTracker.Record("cgt.r4.InfGtPos", CgtFloat(float.PositiveInfinity, 1000.0f));
        TestTracker.Record("cgt.r4.PosGtNegInf", CgtFloat(1.0f, float.NegativeInfinity));

        // float64
        TestTracker.Record("cgt.r8.Greater", CgtDouble(2.0, 1.0));
        TestTracker.Record("cgt.r8.NaN", !CgtDouble(double.NaN, 1.0));
        TestTracker.Record("cgt.r8.InfGtPos", CgtDouble(double.PositiveInfinity, 1000.0));
        TestTracker.Record("cgt.r8.NotLess", !CgtDouble(1.0, 2.0));
        TestTracker.Record("cgt.r8.NotEqual", !CgtDouble(1.0, 1.0));
        TestTracker.Record("cgt.r8.PosGtNegInf", CgtDouble(1.0, double.NegativeInfinity));
        TestTracker.Record("cgt.r8.MaxGtMin", CgtDouble(double.MaxValue, double.MinValue));
        TestTracker.Record("cgt.r8.SmallDiff", CgtDouble(1.0000001, 1.0));

        // byte/short comparisons
        TestTracker.Record("cgt.byte.Greater", CgtByte(200, 100));
        TestTracker.Record("cgt.byte.NotGreater", !CgtByte(100, 200));
        TestTracker.Record("cgt.byte.Equal", !CgtByte(100, 100));
        TestTracker.Record("cgt.byte.MaxGtZero", CgtByte(255, 0));
        TestTracker.Record("cgt.sbyte.Greater", CgtSByte(50, -50));
        TestTracker.Record("cgt.sbyte.NegGreater", CgtSByte(-10, -50));
        TestTracker.Record("cgt.sbyte.ZeroGtNeg", CgtSByte(0, -1));
        TestTracker.Record("cgt.short.Greater", CgtShort(1000, 500));
        TestTracker.Record("cgt.short.NegLesser", !CgtShort(-1000, 500));
        TestTracker.Record("cgt.short.MaxGtMin", CgtShort(short.MaxValue, short.MinValue));
    }

    private static bool Cgt32(int a, int b) => a > b;
    private static bool Cgt64(long a, long b) => a > b;
    private static bool CgtFloat(float a, float b) => a > b;
    private static bool CgtDouble(double a, double b) => a > b;
    private static bool CgtByte(byte a, byte b) => a > b;
    private static bool CgtSByte(sbyte a, sbyte b) => a > b;
    private static bool CgtShort(short a, short b) => a > b;

    #endregion

    #region cgt.un (0xFE 0x03)

    private static void TestCgtUn()
    {
        // uint32 - unsigned comparison
        TestTracker.Record("cgt.un.i4.Basic", CgtUn32(10u, 5u));
        TestTracker.Record("cgt.un.i4.NotGreater", !CgtUn32(5u, 10u));
        TestTracker.Record("cgt.un.i4.Equal", !CgtUn32(5u, 5u));
        TestTracker.Record("cgt.un.i4.HighBit", CgtUn32(0x80000000u, 0x7FFFFFFFu));
        TestTracker.Record("cgt.un.i4.Max", CgtUn32(0xFFFFFFFFu, 0xFFFFFFFEu));
        TestTracker.Record("cgt.un.i4.MaxNotZero", CgtUn32(0xFFFFFFFFu, 0u));
        TestTracker.Record("cgt.un.i4.ZeroNotMax", !CgtUn32(0u, 0xFFFFFFFFu));

        // uint64
        TestTracker.Record("cgt.un.i8.Basic", CgtUn64(100UL, 50UL));
        TestTracker.Record("cgt.un.i8.HighBit", CgtUn64(0x8000000000000000UL, 0x7FFFFFFFFFFFFFFFUL));
        TestTracker.Record("cgt.un.i8.Max", CgtUn64(ulong.MaxValue, ulong.MaxValue - 1));

        // For floats, cgt.un returns true for unordered (NaN) comparisons
        TestTracker.Record("cgt.un.r4.Greater", CgtUnFloat(2.0f, 1.0f));
        TestTracker.Record("cgt.un.r4.NaN", CgtUnFloat(float.NaN, 1.0f)); // true for unordered
        TestTracker.Record("cgt.un.r4.NaNRight", CgtUnFloat(1.0f, float.NaN));
        TestTracker.Record("cgt.un.r4.NaNBoth", CgtUnFloat(float.NaN, float.NaN));

        TestTracker.Record("cgt.un.r8.NaN", CgtUnDouble(double.NaN, 1.0));
        TestTracker.Record("cgt.un.r8.NaNRight", CgtUnDouble(1.0, double.NaN));
        TestTracker.Record("cgt.un.r8.NaNBoth", CgtUnDouble(double.NaN, double.NaN));

        // More unsigned comparisons
        TestTracker.Record("cgt.un.i4.Large", CgtUn32(0xFFFFFFFEu, 0xFFFFFFFDu));
        TestTracker.Record("cgt.un.i4.SmallDiff", CgtUn32(1001u, 1000u));
        TestTracker.Record("cgt.un.i8.Large", CgtUn64(0xFFFFFFFFFFFFFFFEUL, 0xFFFFFFFFFFFFFFFDUL));
        TestTracker.Record("cgt.un.i8.SmallDiff", CgtUn64(1001UL, 1000UL));

        // ushort comparisons
        TestTracker.Record("cgt.un.ushort.Greater", CgtUShort(50000, 30000));
        TestTracker.Record("cgt.un.ushort.NotGreater", !CgtUShort(30000, 50000));
        TestTracker.Record("cgt.un.ushort.Equal", !CgtUShort(50000, 50000));
        TestTracker.Record("cgt.un.ushort.MaxGtZero", CgtUShort(ushort.MaxValue, 0));
    }

    private static bool CgtUn32(uint a, uint b) => a > b;
    private static bool CgtUn64(ulong a, ulong b) => a > b;
    private static bool CgtUnFloat(float a, float b) => !(a <= b); // Equivalent to cgt.un for floats
    private static bool CgtUnDouble(double a, double b) => !(a <= b);
    private static bool CgtUShort(ushort a, ushort b) => a > b;

    #endregion

    #region clt (0xFE 0x04)

    private static void TestClt()
    {
        // int32 basic
        TestTracker.Record("clt.i4.Less", Clt32(5, 10));
        TestTracker.Record("clt.i4.NotLess", !Clt32(10, 5));
        TestTracker.Record("clt.i4.Equal", !Clt32(5, 5));
        TestTracker.Record("clt.i4.Negative", Clt32(-10, -5));
        TestTracker.Record("clt.i4.NegLtZero", Clt32(-1, 0));
        TestTracker.Record("clt.i4.ZeroLtPos", Clt32(0, 1));
        TestTracker.Record("clt.i4.MinLtMax", Clt32(int.MinValue, int.MaxValue));
        TestTracker.Record("clt.i4.MaxNotLtMin", !Clt32(int.MaxValue, int.MinValue));
        TestTracker.Record("clt.i4.MaxLtMaxPlusOne", Clt32(int.MaxValue - 1, int.MaxValue));

        // int64
        TestTracker.Record("clt.i8.Less", Clt64(50L, 100L));
        TestTracker.Record("clt.i8.NotLess", !Clt64(100L, 50L));
        TestTracker.Record("clt.i8.Equal", !Clt64(100L, 100L));
        TestTracker.Record("clt.i8.Large", Clt64(0x100000000L, 0x200000000L));

        // float32
        TestTracker.Record("clt.r4.Less", CltFloat(1.0f, 2.0f));
        TestTracker.Record("clt.r4.NotLess", !CltFloat(2.0f, 1.0f));
        TestTracker.Record("clt.r4.Equal", !CltFloat(1.0f, 1.0f));
        TestTracker.Record("clt.r4.NaN", !CltFloat(float.NaN, 1.0f)); // NaN comparisons are false
        TestTracker.Record("clt.r4.NaNRight", !CltFloat(1.0f, float.NaN));
        TestTracker.Record("clt.r4.NegInfLtPos", CltFloat(float.NegativeInfinity, 1.0f));
        TestTracker.Record("clt.r4.PosLtInf", CltFloat(1000.0f, float.PositiveInfinity));

        // float64
        TestTracker.Record("clt.r8.Less", CltDouble(1.0, 2.0));
        TestTracker.Record("clt.r8.NaN", !CltDouble(double.NaN, 1.0));
        TestTracker.Record("clt.r8.NotGreater", !CltDouble(2.0, 1.0));
        TestTracker.Record("clt.r8.NotEqual", !CltDouble(1.0, 1.0));
        TestTracker.Record("clt.r8.NegInfLtPos", CltDouble(double.NegativeInfinity, 1.0));
        TestTracker.Record("clt.r8.PosLtInf", CltDouble(1000.0, double.PositiveInfinity));
        TestTracker.Record("clt.r8.MinLtMax", CltDouble(double.MinValue, double.MaxValue));
        TestTracker.Record("clt.r8.SmallDiff", CltDouble(1.0, 1.0000001));

        // byte/short comparisons
        TestTracker.Record("clt.byte.Less", CltByte(100, 200));
        TestTracker.Record("clt.byte.NotLess", !CltByte(200, 100));
        TestTracker.Record("clt.byte.Equal", !CltByte(100, 100));
        TestTracker.Record("clt.byte.ZeroLtMax", CltByte(0, 255));
        TestTracker.Record("clt.sbyte.Less", CltSByte(-50, 50));
        TestTracker.Record("clt.sbyte.NegLess", CltSByte(-50, -10));
        TestTracker.Record("clt.sbyte.NegLtZero", CltSByte(-1, 0));
        TestTracker.Record("clt.short.Less", CltShort(500, 1000));
        TestTracker.Record("clt.short.NegLess", CltShort(-1000, 500));
        TestTracker.Record("clt.short.MinLtMax", CltShort(short.MinValue, short.MaxValue));
    }

    private static bool Clt32(int a, int b) => a < b;
    private static bool Clt64(long a, long b) => a < b;
    private static bool CltFloat(float a, float b) => a < b;
    private static bool CltDouble(double a, double b) => a < b;
    private static bool CltByte(byte a, byte b) => a < b;
    private static bool CltSByte(sbyte a, sbyte b) => a < b;
    private static bool CltShort(short a, short b) => a < b;

    #endregion

    #region clt.un (0xFE 0x05)

    private static void TestCltUn()
    {
        // uint32 - unsigned comparison
        TestTracker.Record("clt.un.i4.Basic", CltUn32(5u, 10u));
        TestTracker.Record("clt.un.i4.NotLess", !CltUn32(10u, 5u));
        TestTracker.Record("clt.un.i4.Equal", !CltUn32(5u, 5u));
        TestTracker.Record("clt.un.i4.HighBit", CltUn32(0x7FFFFFFFu, 0x80000000u));
        TestTracker.Record("clt.un.i4.ZeroLtMax", CltUn32(0u, 0xFFFFFFFFu));
        TestTracker.Record("clt.un.i4.MaxNotLtZero", !CltUn32(0xFFFFFFFFu, 0u));

        // uint64
        TestTracker.Record("clt.un.i8.Basic", CltUn64(50UL, 100UL));
        TestTracker.Record("clt.un.i8.HighBit", CltUn64(0x7FFFFFFFFFFFFFFFUL, 0x8000000000000000UL));

        // For floats, clt.un returns true for unordered (NaN) comparisons
        TestTracker.Record("clt.un.r4.Less", CltUnFloat(1.0f, 2.0f));
        TestTracker.Record("clt.un.r4.NaN", CltUnFloat(float.NaN, 1.0f)); // true for unordered
        TestTracker.Record("clt.un.r4.NaNRight", CltUnFloat(1.0f, float.NaN));
        TestTracker.Record("clt.un.r4.NaNBoth", CltUnFloat(float.NaN, float.NaN));

        TestTracker.Record("clt.un.r8.NaN", CltUnDouble(double.NaN, 1.0));
        TestTracker.Record("clt.un.r8.NaNRight", CltUnDouble(1.0, double.NaN));
        TestTracker.Record("clt.un.r8.NaNBoth", CltUnDouble(double.NaN, double.NaN));

        // More unsigned comparisons
        TestTracker.Record("clt.un.i4.Large", CltUn32(0xFFFFFFFDu, 0xFFFFFFFEu));
        TestTracker.Record("clt.un.i4.SmallDiff", CltUn32(1000u, 1001u));
        TestTracker.Record("clt.un.i8.Large", CltUn64(0xFFFFFFFFFFFFFFFDUL, 0xFFFFFFFFFFFFFFFEUL));
        TestTracker.Record("clt.un.i8.SmallDiff", CltUn64(1000UL, 1001UL));

        // ushort comparisons
        TestTracker.Record("clt.un.ushort.Less", CltUShort(30000, 50000));
        TestTracker.Record("clt.un.ushort.NotLess", !CltUShort(50000, 30000));
        TestTracker.Record("clt.un.ushort.Equal", !CltUShort(50000, 50000));
        TestTracker.Record("clt.un.ushort.ZeroLtMax", CltUShort(0, ushort.MaxValue));
    }

    private static bool CltUn32(uint a, uint b) => a < b;
    private static bool CltUn64(ulong a, ulong b) => a < b;
    private static bool CltUnFloat(float a, float b) => !(a >= b); // Equivalent to clt.un for floats
    private static bool CltUnDouble(double a, double b) => !(a >= b);
    private static bool CltUShort(ushort a, ushort b) => a < b;

    #endregion
}
