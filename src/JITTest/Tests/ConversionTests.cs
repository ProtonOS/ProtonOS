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
        // Basic values
        TestTracker.Record("conv.i1.Zero", ConvI1(0) == 0);
        TestTracker.Record("conv.i1.One", ConvI1(1) == 1);
        TestTracker.Record("conv.i1.Max", ConvI1(127) == 127);
        TestTracker.Record("conv.i1.Min", ConvI1(-128) == -128);
        TestTracker.Record("conv.i1.Negative", ConvI1(-1) == -1);

        // Wrapping/truncation
        TestTracker.Record("conv.i1.Wrap128", unchecked((sbyte)128) == -128);
        TestTracker.Record("conv.i1.Wrap255", unchecked((sbyte)255) == -1);
        TestTracker.Record("conv.i1.Wrap256", unchecked((sbyte)256) == 0);
        TestTracker.Record("conv.i1.WrapNeg129", unchecked((sbyte)(-129)) == 127);
        TestTracker.Record("conv.i1.LargeTruncate", unchecked((sbyte)0x12345678) == 0x78);

        // From int64
        TestTracker.Record("conv.i1.FromI8", ConvI1FromI8(100L) == 100);
        TestTracker.Record("conv.i1.FromI8Neg", ConvI1FromI8(-50L) == -50);
        TestTracker.Record("conv.i1.FromI8Large", unchecked((sbyte)0x123456789ABCDEF0L) == unchecked((sbyte)0xF0));

        // Additional edge cases
        TestTracker.Record("conv.i1.FromI4.MaxByte", unchecked((sbyte)127) == 127);
        TestTracker.Record("conv.i1.FromI4.MinByte", unchecked((sbyte)(-128)) == -128);
        TestTracker.Record("conv.i1.FromI4.JustOver", unchecked((sbyte)128) == -128);
        TestTracker.Record("conv.i1.FromI4.JustUnder", unchecked((sbyte)(-129)) == 127);
        TestTracker.Record("conv.i1.FromI4.LowNibble", ConvI1(0x0F) == 0x0F);
        TestTracker.Record("conv.i1.FromI4.HighNibble", ConvI1(0x70) == 0x70);
    }

    private static sbyte ConvI1(int x) => (sbyte)x;
    private static sbyte ConvI1FromI8(long x) => (sbyte)x;

    #endregion

    #region conv.u1 (0xD2)

    private static void TestConvU1()
    {
        // Basic values
        TestTracker.Record("conv.u1.Zero", ConvU1(0) == 0);
        TestTracker.Record("conv.u1.One", ConvU1(1) == 1);
        TestTracker.Record("conv.u1.Max", ConvU1(255) == 255);

        // Truncation
        TestTracker.Record("conv.u1.Truncate256", unchecked((byte)256) == 0);
        TestTracker.Record("conv.u1.Truncate257", unchecked((byte)257) == 1);
        TestTracker.Record("conv.u1.FromNegative", unchecked((byte)(-1)) == 255);
        TestTracker.Record("conv.u1.FromNeg128", unchecked((byte)(-128)) == 128);
        TestTracker.Record("conv.u1.LargeTruncate", unchecked((byte)0x12345678) == 0x78);

        // From int64
        TestTracker.Record("conv.u1.FromI8", ConvU1FromI8(200L) == 200);
        TestTracker.Record("conv.u1.FromI8Neg", unchecked((byte)(-1L)) == 255);

        // Additional edge cases
        TestTracker.Record("conv.u1.FromI4.MaxByte", ConvU1(255) == 255);
        TestTracker.Record("conv.u1.FromI4.JustOver", unchecked((byte)256) == 0);
        TestTracker.Record("conv.u1.FromI4.JustUnder", ConvU1(254) == 254);
        TestTracker.Record("conv.u1.FromI4.LowNibble", ConvU1(0x0F) == 0x0F);
        TestTracker.Record("conv.u1.FromI4.HighNibble", ConvU1(0xF0) == 0xF0);
        TestTracker.Record("conv.u1.FromI4.AllBits", ConvU1(0xFF) == 0xFF);
    }

    private static byte ConvU1(int x) => (byte)x;
    private static byte ConvU1FromI8(long x) => (byte)x;

    #endregion

    #region conv.i2 (0x68)

    private static void TestConvI2()
    {
        // Basic values
        TestTracker.Record("conv.i2.Zero", ConvI2(0) == 0);
        TestTracker.Record("conv.i2.One", ConvI2(1) == 1);
        TestTracker.Record("conv.i2.Max", ConvI2(32767) == 32767);
        TestTracker.Record("conv.i2.Min", ConvI2(-32768) == -32768);
        TestTracker.Record("conv.i2.Negative", ConvI2(-1) == -1);

        // Wrapping
        TestTracker.Record("conv.i2.Wrap32768", unchecked((short)32768) == -32768);
        TestTracker.Record("conv.i2.Wrap65535", unchecked((short)65535) == -1);
        TestTracker.Record("conv.i2.Wrap65536", unchecked((short)65536) == 0);
        TestTracker.Record("conv.i2.LargeTruncate", unchecked((short)0x12345678) == 0x5678);

        // From int64
        TestTracker.Record("conv.i2.FromI8", ConvI2FromI8(1000L) == 1000);
        TestTracker.Record("conv.i2.FromI8Neg", ConvI2FromI8(-500L) == -500);

        // Additional edge cases
        TestTracker.Record("conv.i2.FromI4.JustOver", unchecked((short)32768) == -32768);
        TestTracker.Record("conv.i2.FromI4.JustUnder", unchecked((short)(-32769)) == 32767);
        TestTracker.Record("conv.i2.FromI4.LowByte", ConvI2(0x00FF) == 0x00FF);
        TestTracker.Record("conv.i2.FromI4.HighByte", ConvI2(0x7F00) == 0x7F00);
        TestTracker.Record("conv.i2.FromI4.AllBits", unchecked((short)0xFFFF) == -1);
        TestTracker.Record("conv.i2.FromI4.MixedBits", ConvI2(0x1234) == 0x1234);
    }

    private static short ConvI2(int x) => (short)x;
    private static short ConvI2FromI8(long x) => (short)x;

    #endregion

    #region conv.u2 (0xD1)

    private static void TestConvU2()
    {
        // Basic values
        TestTracker.Record("conv.u2.Zero", ConvU2(0) == 0);
        TestTracker.Record("conv.u2.One", ConvU2(1) == 1);
        TestTracker.Record("conv.u2.Max", ConvU2(65535) == 65535);

        // Truncation
        TestTracker.Record("conv.u2.Truncate65536", unchecked((ushort)65536) == 0);
        TestTracker.Record("conv.u2.Truncate65537", unchecked((ushort)65537) == 1);
        TestTracker.Record("conv.u2.FromNegative", unchecked((ushort)(-1)) == 65535);
        TestTracker.Record("conv.u2.LargeTruncate", unchecked((ushort)0x12345678) == 0x5678);

        // From int64
        TestTracker.Record("conv.u2.FromI8", ConvU2FromI8(50000L) == 50000);

        // Additional edge cases
        TestTracker.Record("conv.u2.FromI4.JustOver", unchecked((ushort)65536) == 0);
        TestTracker.Record("conv.u2.FromI4.JustUnder", ConvU2(65534) == 65534);
        TestTracker.Record("conv.u2.FromI4.LowByte", ConvU2(0x00FF) == 0x00FF);
        TestTracker.Record("conv.u2.FromI4.HighByte", ConvU2(0xFF00) == 0xFF00);
        TestTracker.Record("conv.u2.FromI4.AllBits", ConvU2(0xFFFF) == 0xFFFF);
        TestTracker.Record("conv.u2.FromI4.MixedBits", ConvU2(0x5678) == 0x5678);
    }

    private static ushort ConvU2(int x) => (ushort)x;
    private static ushort ConvU2FromI8(long x) => (ushort)x;

    #endregion

    #region conv.i4 (0x69)

    private static void TestConvI4()
    {
        // From int64
        TestTracker.Record("conv.i4.FromI8", ConvI4FromI8(100L) == 100);
        TestTracker.Record("conv.i4.FromI8Neg", ConvI4FromI8(-100L) == -100);
        TestTracker.Record("conv.i4.FromI8Max", ConvI4FromI8((long)int.MaxValue) == int.MaxValue);
        TestTracker.Record("conv.i4.FromI8Min", ConvI4FromI8((long)int.MinValue) == int.MinValue);
        TestTracker.Record("conv.i4.Truncate", unchecked((int)0x100000000L) == 0);
        TestTracker.Record("conv.i4.TruncateLarge", unchecked((int)0x123456789ABCDEF0L) == unchecked((int)0x9ABCDEF0));

        // From float - truncates toward zero
        TestTracker.Record("conv.i4.FromR4Pos", ConvI4FromR4(3.9f) == 3);
        TestTracker.Record("conv.i4.FromR4Neg", ConvI4FromR4(-3.9f) == -3);
        TestTracker.Record("conv.i4.FromR4Zero", ConvI4FromR4(0.0f) == 0);
        TestTracker.Record("conv.i4.FromR4Half", ConvI4FromR4(0.5f) == 0);
        TestTracker.Record("conv.i4.FromR4NegHalf", ConvI4FromR4(-0.5f) == 0);

        // From double
        TestTracker.Record("conv.i4.FromR8Pos", ConvI4FromR8(3.9) == 3);
        TestTracker.Record("conv.i4.FromR8Neg", ConvI4FromR8(-3.9) == -3);
        TestTracker.Record("conv.i4.FromR8Large", ConvI4FromR8(1000000.999) == 1000000);
    }

    private static int ConvI4FromI8(long x) => (int)x;
    private static int ConvI4FromR4(float x) => (int)x;
    private static int ConvI4FromR8(double x) => (int)x;

    #endregion

    #region conv.u4 (0x6D)

    private static void TestConvU4()
    {
        // From int64
        TestTracker.Record("conv.u4.FromI8", ConvU4FromI8(100L) == 100u);
        TestTracker.Record("conv.u4.FromI8Large", ConvU4FromI8(0xFFFFFFFFL) == 0xFFFFFFFFu);
        TestTracker.Record("conv.u4.Truncate", unchecked((uint)0x100000000L) == 0u);
        TestTracker.Record("conv.u4.FromNegI8", unchecked((uint)(-1L)) == 0xFFFFFFFFu);

        // From float
        TestTracker.Record("conv.u4.FromR4", ConvU4FromR4(100.9f) == 100u);
        TestTracker.Record("conv.u4.FromR4Large", ConvU4FromR4(3000000000.0f) == 3000000000u);
    }

    private static uint ConvU4FromI8(long x) => (uint)x;
    private static uint ConvU4FromR4(float x) => (uint)x;

    #endregion

    #region conv.i8 (0x6A)

    private static void TestConvI8()
    {
        // From int32 - sign extends
        TestTracker.Record("conv.i8.FromI4Pos", ConvI8FromI4(100) == 100L);
        TestTracker.Record("conv.i8.FromI4Neg", ConvI8FromI4(-1) == -1L);
        TestTracker.Record("conv.i8.FromI4Max", ConvI8FromI4(int.MaxValue) == (long)int.MaxValue);
        TestTracker.Record("conv.i8.FromI4Min", ConvI8FromI4(int.MinValue) == (long)int.MinValue);
        TestTracker.Record("conv.i8.SignExtend", ConvI8FromI4(-1) == -1L);
        TestTracker.Record("conv.i8.SignExtendBits", ConvI8FromI4(-1) == unchecked((long)0xFFFFFFFFFFFFFFFFL));

        // From uint32 - should zero extend
        TestTracker.Record("conv.i8.FromU4", ConvI8FromU4(100u) == 100L);
        TestTracker.Record("conv.i8.FromU4Max", ConvI8FromU4(uint.MaxValue) == (long)uint.MaxValue);
        TestTracker.Record("conv.i8.FromU4HighBit", ConvI8FromU4(0x80000000u) == 0x80000000L);

        // From float
        TestTracker.Record("conv.i8.FromR4", ConvI8FromR4(3.9f) == 3L);
        TestTracker.Record("conv.i8.FromR4Neg", ConvI8FromR4(-3.9f) == -3L);
        TestTracker.Record("conv.i8.FromR8", ConvI8FromR8(3.9) == 3L);
        TestTracker.Record("conv.i8.FromR8Large", ConvI8FromR8(10000000000.5) == 10000000000L);
    }

    private static long ConvI8FromI4(int x) => (long)x;
    private static long ConvI8FromU4(uint x) => (long)x;
    private static long ConvI8FromR4(float x) => (long)x;
    private static long ConvI8FromR8(double x) => (long)x;

    #endregion

    #region conv.u8 (0x6E)

    private static void TestConvU8()
    {
        // From int32
        TestTracker.Record("conv.u8.FromI4Pos", ConvU8FromI4(100) == 100UL);
        TestTracker.Record("conv.u8.FromI4Zero", ConvU8FromI4(0) == 0UL);

        // From negative int32 - reinterprets bits
        TestTracker.Record("conv.u8.FromNegI4", unchecked((ulong)(-1)) == ulong.MaxValue);
        TestTracker.Record("conv.u8.FromNegI4Bits", unchecked((ulong)(-1)) == 0xFFFFFFFFFFFFFFFFUL);

        // From uint32
        TestTracker.Record("conv.u8.FromU4", ConvU8FromU4(100u) == 100UL);
        TestTracker.Record("conv.u8.FromU4Max", ConvU8FromU4(uint.MaxValue) == (ulong)uint.MaxValue);

        // From float
        TestTracker.Record("conv.u8.FromR4", ConvU8FromR4(100.9f) == 100UL);
        TestTracker.Record("conv.u8.FromR8", ConvU8FromR8(100.9) == 100UL);
    }

    private static ulong ConvU8FromI4(int x) => (ulong)x;
    private static ulong ConvU8FromU4(uint x) => (ulong)x;
    private static ulong ConvU8FromR4(float x) => (ulong)x;
    private static ulong ConvU8FromR8(double x) => (ulong)x;

    #endregion

    #region conv.r4 (0x6B)

    private static void TestConvR4()
    {
        // From int32
        TestTracker.Record("conv.r4.FromI4Zero", Assert.AreApproxEqual(0.0f, ConvR4FromI4(0)));
        TestTracker.Record("conv.r4.FromI4Pos", Assert.AreApproxEqual(100.0f, ConvR4FromI4(100)));
        TestTracker.Record("conv.r4.FromI4Neg", Assert.AreApproxEqual(-100.0f, ConvR4FromI4(-100)));
        TestTracker.Record("conv.r4.FromI4Max", ConvR4FromI4(int.MaxValue) > 2000000000.0f);
        TestTracker.Record("conv.r4.FromI4Min", ConvR4FromI4(int.MinValue) < -2000000000.0f);

        // From int64
        TestTracker.Record("conv.r4.FromI8", Assert.AreApproxEqual(100.0f, ConvR4FromI8(100L)));
        // Note: Using 1000000000L (1 billion) for better float precision (float has ~7 digits)
        TestTracker.Record("conv.r4.FromI8Large", ConvR4FromI8(1000000000L) > 900000000.0f);

        // From uint32
        TestTracker.Record("conv.r4.FromU4", Assert.AreApproxEqual(100.0f, ConvR4FromU4(100u)));
        TestTracker.Record("conv.r4.FromU4Max", ConvR4FromU4(uint.MaxValue) > 4000000000.0f);

        // From double - may lose precision
        TestTracker.Record("conv.r4.FromR8", Assert.AreApproxEqual(3.14f, ConvR4FromR8(3.14), 0.01f));
        TestTracker.Record("conv.r4.FromR8.Zero", Assert.AreApproxEqual(0.0f, ConvR4FromR8(0.0)));
        TestTracker.Record("conv.r4.FromR8.NegOne", Assert.AreApproxEqual(-1.0f, ConvR4FromR8(-1.0)));
        TestTracker.Record("conv.r4.FromR8.Large", ConvR4FromR8(1000000000.0) > 900000000.0f);

        // From uint64
        TestTracker.Record("conv.r4.FromU8", Assert.AreApproxEqual(100.0f, ConvR4FromU8(100UL)));
        TestTracker.Record("conv.r4.FromU8.Large", ConvR4FromU8(1000000000UL) > 900000000.0f);

        // Special float values
        TestTracker.Record("conv.r4.FromI4.One", Assert.AreApproxEqual(1.0f, ConvR4FromI4(1)));
        TestTracker.Record("conv.r4.FromI4.NegOne", Assert.AreApproxEqual(-1.0f, ConvR4FromI4(-1)));
        TestTracker.Record("conv.r4.FromI4.Large", ConvR4FromI4(1000000) > 999999.0f);
    }

    private static float ConvR4FromI4(int x) => (float)x;
    private static float ConvR4FromI8(long x) => (float)x;
    private static float ConvR4FromU4(uint x) => (float)x;
    private static float ConvR4FromR8(double x) => (float)x;
    private static float ConvR4FromU8(ulong x) => (float)x;

    #endregion

    #region conv.r8 (0x6C)

    private static void TestConvR8()
    {
        // From int32
        TestTracker.Record("conv.r8.FromI4Zero", Assert.AreApproxEqual(0.0, ConvR8FromI4(0)));
        TestTracker.Record("conv.r8.FromI4Pos", Assert.AreApproxEqual(100.0, ConvR8FromI4(100)));
        TestTracker.Record("conv.r8.FromI4Neg", Assert.AreApproxEqual(-100.0, ConvR8FromI4(-100)));
        TestTracker.Record("conv.r8.FromI4Max", Assert.AreApproxEqual((double)int.MaxValue, ConvR8FromI4(int.MaxValue)));

        // From int64
        TestTracker.Record("conv.r8.FromI8", Assert.AreApproxEqual(100.0, ConvR8FromI8(100L)));
        TestTracker.Record("conv.r8.FromI8Large", ConvR8FromI8(1000000000000L) > 999999999999.0);

        // From uint32
        TestTracker.Record("conv.r8.FromU4", Assert.AreApproxEqual(100.0, ConvR8FromU4(100u)));
        TestTracker.Record("conv.r8.FromU4Max", Assert.AreApproxEqual((double)uint.MaxValue, ConvR8FromU4(uint.MaxValue)));

        // From float - no precision loss
        TestTracker.Record("conv.r8.FromR4", Assert.AreApproxEqual(3.14, ConvR8FromR4(3.14f), 0.01));
        TestTracker.Record("conv.r8.FromR4.Zero", Assert.AreApproxEqual(0.0, ConvR8FromR4(0.0f)));
        TestTracker.Record("conv.r8.FromR4.NegOne", Assert.AreApproxEqual(-1.0, ConvR8FromR4(-1.0f)));
        TestTracker.Record("conv.r8.FromR4.Large", ConvR8FromR4(1000000.0f) > 999999.0);

        // From uint64
        TestTracker.Record("conv.r8.FromU8", Assert.AreApproxEqual(100.0, ConvR8FromU8(100UL)));
        TestTracker.Record("conv.r8.FromU8.Large", ConvR8FromU8(1000000000000UL) > 999999999999.0);

        // Additional edge cases
        TestTracker.Record("conv.r8.FromI4.One", Assert.AreApproxEqual(1.0, ConvR8FromI4(1)));
        TestTracker.Record("conv.r8.FromI4.NegOne", Assert.AreApproxEqual(-1.0, ConvR8FromI4(-1)));
        TestTracker.Record("conv.r8.FromI8.One", Assert.AreApproxEqual(1.0, ConvR8FromI8(1L)));
        TestTracker.Record("conv.r8.FromI8.NegOne", Assert.AreApproxEqual(-1.0, ConvR8FromI8(-1L)));
    }

    private static double ConvR8FromI4(int x) => (double)x;
    private static double ConvR8FromI8(long x) => (double)x;
    private static double ConvR8FromU4(uint x) => (double)x;
    private static double ConvR8FromR4(float x) => (double)x;
    private static double ConvR8FromU8(ulong x) => (double)x;

    #endregion

    #region conv.i / conv.u (0xD3, 0xE0)

    private static void TestConvI()
    {
        // conv.i - convert to native int
        TestTracker.Record("conv.i.FromI4Pos", ConvIFromI4(100) == 100);
        TestTracker.Record("conv.i.FromI4Neg", ConvIFromI4(-100) == -100);
        TestTracker.Record("conv.i.FromI4Zero", ConvIFromI4(0) == 0);
        TestTracker.Record("conv.i.FromI8", ConvIFromI8(100L) == 100);
        TestTracker.Record("conv.i.FromI8Neg", ConvIFromI8(-100L) == -100);
        TestTracker.Record("conv.i.FromU4", ConvIFromU4(100u) == 100);

        // Additional native int tests
        TestTracker.Record("conv.i.FromI4.Max", ConvIFromI4(int.MaxValue) == int.MaxValue);
        TestTracker.Record("conv.i.FromI4.Min", ConvIFromI4(int.MinValue) == int.MinValue);
        TestTracker.Record("conv.i.FromI4.One", ConvIFromI4(1) == 1);
        TestTracker.Record("conv.i.FromI4.NegOne", ConvIFromI4(-1) == -1);
        TestTracker.Record("conv.i.FromI8.Zero", ConvIFromI8(0L) == 0);
        TestTracker.Record("conv.i.FromU4.Zero", ConvIFromU4(0u) == 0);
        TestTracker.Record("conv.i.FromU4.Large", ConvIFromU4(0x7FFFFFFFu) == 0x7FFFFFFF);
    }

    private static void TestConvU()
    {
        // conv.u - convert to native unsigned int
        TestTracker.Record("conv.u.FromI4", ConvUFromI4(100) == 100u);
        TestTracker.Record("conv.u.FromI4Zero", ConvUFromI4(0) == 0u);
        TestTracker.Record("conv.u.FromU4", ConvUFromU4(100u) == 100u);
        TestTracker.Record("conv.u.FromU4Max", ConvUFromU4(uint.MaxValue) == uint.MaxValue);

        // Additional native unsigned int tests
        TestTracker.Record("conv.u.FromI4.One", ConvUFromI4(1) == 1u);
        TestTracker.Record("conv.u.FromI4.Large", ConvUFromI4(0x7FFFFFFF) == 0x7FFFFFFFu);
        TestTracker.Record("conv.u.FromU4.One", ConvUFromU4(1u) == 1u);
        TestTracker.Record("conv.u.FromU4.Zero", ConvUFromU4(0u) == 0u);
        TestTracker.Record("conv.u.FromU8", ConvUFromU8(100UL) == 100u);
        TestTracker.Record("conv.u.FromU8.Zero", ConvUFromU8(0UL) == 0u);
        TestTracker.Record("conv.u.FromU8.Large", ConvUFromU8(0xFFFFFFFFUL) == 0xFFFFFFFFu);
    }

    private static nint ConvIFromI4(int x) => (nint)x;
    private static nint ConvIFromI8(long x) => (nint)x;
    private static nint ConvIFromU4(uint x) => (nint)x;
    private static nuint ConvUFromI4(int x) => (nuint)x;
    private static nuint ConvUFromU4(uint x) => (nuint)x;
    private static nuint ConvUFromU8(ulong x) => (nuint)x;

    #endregion

    #region Overflow-checked conversions

    private static void TestConvOvf()
    {
        // conv.ovf.i1 - int32 to sbyte with overflow check
        TestTracker.Record("conv.ovf.i1.InRange", ConvOvfI1(100) == 100);
        TestTracker.Record("conv.ovf.i1.Max", ConvOvfI1(127) == 127);
        TestTracker.Record("conv.ovf.i1.Min", ConvOvfI1(-128) == -128);
        TestTracker.Record("conv.ovf.i1.Zero", ConvOvfI1(0) == 0);

        // conv.ovf.u1 - int32 to byte with overflow check
        TestTracker.Record("conv.ovf.u1.InRange", ConvOvfU1(200) == 200);
        TestTracker.Record("conv.ovf.u1.Max", ConvOvfU1(255) == 255);
        TestTracker.Record("conv.ovf.u1.Zero", ConvOvfU1(0) == 0);

        // conv.ovf.i2 - int32 to short with overflow check
        TestTracker.Record("conv.ovf.i2.InRange", ConvOvfI2(1000) == 1000);
        TestTracker.Record("conv.ovf.i2.Max", ConvOvfI2(32767) == 32767);
        TestTracker.Record("conv.ovf.i2.Min", ConvOvfI2(-32768) == -32768);

        // conv.ovf.u2 - int32 to ushort with overflow check
        TestTracker.Record("conv.ovf.u2.InRange", ConvOvfU2(50000) == 50000);
        TestTracker.Record("conv.ovf.u2.Max", ConvOvfU2(65535) == 65535);

        // conv.ovf.i4 - int64 to int32 with overflow check
        TestTracker.Record("conv.ovf.i4.InRange", ConvOvfI4(100L) == 100);
        TestTracker.Record("conv.ovf.i4.Max", ConvOvfI4((long)int.MaxValue) == int.MaxValue);
        TestTracker.Record("conv.ovf.i4.Min", ConvOvfI4((long)int.MinValue) == int.MinValue);

        // conv.ovf.u4 - int64 to uint32 with overflow check
        TestTracker.Record("conv.ovf.u4.InRange", ConvOvfU4(100L) == 100u);
        TestTracker.Record("conv.ovf.u4.Max", ConvOvfU4((long)uint.MaxValue) == uint.MaxValue);

        // conv.ovf.i8 - already int64, just validates
        TestTracker.Record("conv.ovf.i8.FromI4", ConvOvfI8(100) == 100L);
        TestTracker.Record("conv.ovf.i8.FromU4", ConvOvfI8FromU4(uint.MaxValue) == (long)uint.MaxValue);

        // conv.ovf.u8 - checks for negative
        TestTracker.Record("conv.ovf.u8.FromI8", ConvOvfU8(100L) == 100UL);
        TestTracker.Record("conv.ovf.u8.FromI4", ConvOvfU8FromI4(100) == 100UL);

        // Additional overflow-checked tests
        TestTracker.Record("conv.ovf.i1.Neg", ConvOvfI1(-50) == -50);
        TestTracker.Record("conv.ovf.i1.One", ConvOvfI1(1) == 1);
        TestTracker.Record("conv.ovf.u1.One", ConvOvfU1(1) == 1);
        TestTracker.Record("conv.ovf.i2.Neg", ConvOvfI2(-500) == -500);
        TestTracker.Record("conv.ovf.i2.One", ConvOvfI2(1) == 1);
        TestTracker.Record("conv.ovf.u2.One", ConvOvfU2(1) == 1);
        TestTracker.Record("conv.ovf.i4.One", ConvOvfI4(1L) == 1);
        TestTracker.Record("conv.ovf.i4.Neg", ConvOvfI4(-1L) == -1);
        TestTracker.Record("conv.ovf.u4.One", ConvOvfU4(1L) == 1u);
        TestTracker.Record("conv.ovf.i8.Neg", ConvOvfI8(-1) == -1L);
        TestTracker.Record("conv.ovf.u8.One", ConvOvfU8(1L) == 1UL);
        TestTracker.Record("conv.ovf.u8.Zero", ConvOvfU8(0L) == 0UL);
    }

    private static sbyte ConvOvfI1(int x) => checked((sbyte)x);
    private static byte ConvOvfU1(int x) => checked((byte)x);
    private static short ConvOvfI2(int x) => checked((short)x);
    private static ushort ConvOvfU2(int x) => checked((ushort)x);
    private static int ConvOvfI4(long x) => checked((int)x);
    private static uint ConvOvfU4(long x) => checked((uint)x);
    private static long ConvOvfI8(int x) => checked((long)x);
    private static long ConvOvfI8FromU4(uint x) => checked((long)x);
    private static ulong ConvOvfU8(long x) => checked((ulong)x);
    private static ulong ConvOvfU8FromI4(int x) => checked((ulong)x);

    #endregion
}
