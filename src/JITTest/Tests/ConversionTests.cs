// JITTest - Conversion Operation Tests
// Section 14: conv.* instructions

namespace JITTest;

/// <summary>
/// Tests for type conversion instructions
/// </summary>
public static class ConversionTests
{
    public static void RunAll()
    {
        // Basic conversions
        TestConvI1();
        TestConvU1();
        TestConvI2();
        TestConvU2();
        TestConvI4();
        TestConvU4();
        TestConvI8();
        TestConvU8();
        TestConvR4();
        TestConvR8();
        TestConvI();
        TestConvU();

        // Overflow-checked conversions
        TestConvOvf();
    }

    #region conv.i1 (0x67)

    private static void TestConvI1()
    {
        TestTracker.Record("conv.i1.Zero", (sbyte)0 == 0);
        TestTracker.Record("conv.i1.Max", (sbyte)127 == 127);
        TestTracker.Record("conv.i1.Wrap128", unchecked((sbyte)128) == -128);
        TestTracker.Record("conv.i1.Wrap255", unchecked((sbyte)255) == -1);
        TestTracker.Record("conv.i1.Negative", (sbyte)(-1) == -1);
    }

    #endregion

    #region conv.u1 (0xD2)

    private static void TestConvU1()
    {
        TestTracker.Record("conv.u1.Zero", (byte)0 == 0);
        TestTracker.Record("conv.u1.Max", (byte)255 == 255);
        TestTracker.Record("conv.u1.Truncate", unchecked((byte)256) == 0);
        TestTracker.Record("conv.u1.FromNegative", unchecked((byte)(-1)) == 255);
    }

    #endregion

    #region conv.i2 (0x68)

    private static void TestConvI2()
    {
        TestTracker.Record("conv.i2.Zero", (short)0 == 0);
        TestTracker.Record("conv.i2.Max", (short)32767 == 32767);
        TestTracker.Record("conv.i2.Wrap", unchecked((short)32768) == -32768);
        TestTracker.Record("conv.i2.Negative", (short)(-1) == -1);
    }

    #endregion

    #region conv.u2 (0xD1)

    private static void TestConvU2()
    {
        TestTracker.Record("conv.u2.Zero", (ushort)0 == 0);
        TestTracker.Record("conv.u2.Max", (ushort)65535 == 65535);
        TestTracker.Record("conv.u2.Truncate", unchecked((ushort)65536) == 0);
    }

    #endregion

    #region conv.i4 (0x69)

    private static void TestConvI4()
    {
        TestTracker.Record("conv.i4.FromI8", (int)100L == 100);
        TestTracker.Record("conv.i4.Truncate", unchecked((int)0x100000000L) == 0);
        TestTracker.Record("conv.i4.FromFloat", (int)3.9f == 3); // Truncate toward zero
        TestTracker.Record("conv.i4.FromNegFloat", (int)(-3.9f) == -3);
    }

    #endregion

    #region conv.u4 (0x6D)

    private static void TestConvU4()
    {
        TestTracker.Record("conv.u4.FromI8", (uint)100L == 100u);
        TestTracker.Record("conv.u4.Truncate", unchecked((uint)0x100000000L) == 0u);
        TestTracker.Record("conv.u4.Max", (uint)0xFFFFFFFFL == 0xFFFFFFFFu);
    }

    #endregion

    #region conv.i8 (0x6A)

    private static void TestConvI8()
    {
        TestTracker.Record("conv.i8.FromI4", (long)100 == 100L);
        TestTracker.Record("conv.i8.SignExtend", (long)(-1) == -1L);
        TestTracker.Record("conv.i8.FromFloat", (long)3.9 == 3L);
    }

    #endregion

    #region conv.u8 (0x6E)

    private static void TestConvU8()
    {
        TestTracker.Record("conv.u8.FromI4", (ulong)100 == 100UL);
        TestTracker.Record("conv.u8.FromNegI4", unchecked((ulong)(-1)) == ulong.MaxValue);
    }

    #endregion

    #region conv.r4 (0x6B)

    private static void TestConvR4()
    {
        TestTracker.Record("conv.r4.FromI4", Assert.AreApproxEqual(100.0f, (float)100));
        TestTracker.Record("conv.r4.FromI8", Assert.AreApproxEqual(100.0f, (float)100L));
        TestTracker.Record("conv.r4.FromR8", Assert.AreApproxEqual(3.14f, (float)3.14, 0.01f));
    }

    #endregion

    #region conv.r8 (0x6C)

    private static void TestConvR8()
    {
        TestTracker.Record("conv.r8.FromI4", Assert.AreApproxEqual(100.0, (double)100));
        TestTracker.Record("conv.r8.FromI8", Assert.AreApproxEqual(100.0, (double)100L));
        TestTracker.Record("conv.r8.FromR4", Assert.AreApproxEqual(3.14, (double)3.14f, 0.01));
    }

    #endregion

    #region conv.i / conv.u (0xD3, 0xE0)

    private static void TestConvI()
    {
        TestTracker.Record("conv.i.FromI4", (nint)100 == 100);
        TestTracker.Record("conv.i.FromI8", (nint)100L == 100);
    }

    private static void TestConvU()
    {
        TestTracker.Record("conv.u.FromI4", (nuint)100 == 100u);
    }

    #endregion

    #region Overflow-checked conversions

    private static void TestConvOvf()
    {
        // These throw on overflow - just test the non-overflow cases
        TestTracker.Record("conv.ovf.i1.InRange", checked((sbyte)100) == 100);
        TestTracker.Record("conv.ovf.u1.InRange", checked((byte)200) == 200);
        TestTracker.Record("conv.ovf.i2.InRange", checked((short)1000) == 1000);
        TestTracker.Record("conv.ovf.u2.InRange", checked((ushort)50000) == 50000);
        TestTracker.Record("conv.ovf.i4.InRange", checked((int)100L) == 100);
        TestTracker.Record("conv.ovf.u4.InRange", checked((uint)100L) == 100u);
    }

    #endregion
}
