// JITTest - Bitwise Operation Tests
// Section 12: and, or, xor, shl, shr, shr.un, not

namespace JITTest;

/// <summary>
/// Tests for bitwise instructions
/// </summary>
public static class BitwiseTests
{
    public static void RunAll()
    {
        TestAnd();
        TestOr();
        TestXor();
        TestNot();
        TestShl();
        TestShr();
        TestShrUn();
    }

    #region and (0x5F)

    private static void TestAnd()
    {
        TestTracker.Record("and.i4.AllOnes", (0xFFFFFFFF & 0xFFFFFFFF) == 0xFFFFFFFF);
        TestTracker.Record("and.i4.AllZeros", (0xFFFFFFFF & 0) == 0);
        TestTracker.Record("and.i4.Mask", (0x12345678 & 0x0000FFFF) == 0x00005678);
        TestTracker.Record("and.i4.Alternating", (0xAAAAAAAA & 0x55555555) == 0);

        TestTracker.Record("and.i8.Basic", (0x123456789ABCDEF0L & 0x0000FFFF0000FFFFL) == 0x0000567800009EF0L);
    }

    #endregion

    #region or (0x60)

    private static void TestOr()
    {
        TestTracker.Record("or.i4.WithZero", (0x12345678 | 0) == 0x12345678);
        TestTracker.Record("or.i4.Combine", (0x000000FF | 0x0000FF00) == 0x0000FFFF);
        TestTracker.Record("or.i4.Alternating", (0xAAAAAAAA | 0x55555555) == 0xFFFFFFFF);

        TestTracker.Record("or.i8.Basic", unchecked((long)(0x00FF00FF00FF00FFul | 0xFF00FF00FF00FF00ul)) == -1L);
    }

    #endregion

    #region xor (0x61)

    private static void TestXor()
    {
        TestTracker.Record("xor.i4.SameValue", (0x12345678 ^ 0x12345678) == 0);
        TestTracker.Record("xor.i4.WithZero", (0x12345678 ^ 0) == 0x12345678);
        TestTracker.Record("xor.i4.Flip", (0xFFFFFFFF ^ 0xAAAAAAAA) == 0x55555555);

        TestTracker.Record("xor.i8.Basic", (0x123456789ABCDEF0L ^ 0x123456789ABCDEF0L) == 0L);
    }

    #endregion

    #region not (0x66)

    private static void TestNot()
    {
        TestTracker.Record("not.i4.Zero", ~0 == -1);
        TestTracker.Record("not.i4.AllOnes", ~(-1) == 0);
        TestTracker.Record("not.i4.Pattern", ~0x0F0F0F0F == unchecked((int)0xF0F0F0F0));

        TestTracker.Record("not.i8.Basic", ~0L == -1L);
    }

    #endregion

    #region shl (0x62)

    private static void TestShl()
    {
        TestTracker.Record("shl.i4.By1", (1 << 1) == 2);
        TestTracker.Record("shl.i4.By4", (1 << 4) == 16);
        TestTracker.Record("shl.i4.By31", (1 << 31) == int.MinValue);
        TestTracker.Record("shl.i4.ShiftOut", (0x80000000 << 1) == 0);

        TestTracker.Record("shl.i8.By32", (1L << 32) == 0x100000000L);
        TestTracker.Record("shl.i8.By63", (1L << 63) == long.MinValue);
    }

    #endregion

    #region shr (0x63) - signed shift right

    private static void TestShr()
    {
        TestTracker.Record("shr.i4.Positive", (16 >> 2) == 4);
        TestTracker.Record("shr.i4.Negative", (-16 >> 2) == -4); // Sign extension
        TestTracker.Record("shr.i4.SignBit", (int.MinValue >> 1) == unchecked((int)0xC0000000));

        TestTracker.Record("shr.i8.Positive", (16L >> 2) == 4L);
        TestTracker.Record("shr.i8.Negative", (-16L >> 2) == -4L);
    }

    #endregion

    #region shr.un (0x64) - unsigned shift right

    private static void TestShrUn()
    {
        TestTracker.Record("shr.un.Positive", ((uint)16 >> 2) == 4u);
        TestTracker.Record("shr.un.HighBit", (0x80000000u >> 1) == 0x40000000u); // No sign extension

        TestTracker.Record("shr.un.i8.HighBit", (0x8000000000000000UL >> 1) == 0x4000000000000000UL);
    }

    #endregion
}
