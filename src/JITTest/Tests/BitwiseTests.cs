// JITTest - Bitwise Operation Tests
// Section 12: and, or, xor, shl, shr, shr.un, not

namespace JITTest;

/// <summary>
/// Tests for bitwise instructions (ECMA-335 Section III.3)
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

        // Small type bitwise operations
        RunSmallTypeTests();
    }

    #region and (0x5F)

    private static void TestAnd()
    {
        // int32 basic tests
        TestTracker.Record("and.i4.ZeroZero", And32(0, 0) == 0);
        TestTracker.Record("and.i4.AllOnes", And32(unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF)) == unchecked((int)0xFFFFFFFF));
        TestTracker.Record("and.i4.AllOnesZero", And32(unchecked((int)0xFFFFFFFF), 0) == 0);
        TestTracker.Record("and.i4.ZeroAllOnes", And32(0, unchecked((int)0xFFFFFFFF)) == 0);
        TestTracker.Record("and.i4.NoCommon", And32(unchecked((int)0x0F0F0F0F), unchecked((int)0xF0F0F0F0)) == 0);
        TestTracker.Record("and.i4.Partial", And32(unchecked((int)0xFF00FF00), unchecked((int)0xFFFF0000)) == unchecked((int)0xFF000000));
        TestTracker.Record("and.i4.Identity", And32(0x12345678, 0x12345678) == 0x12345678);
        TestTracker.Record("and.i4.WithAllOnes", And32(0x12345678, unchecked((int)0xFFFFFFFF)) == 0x12345678);
        TestTracker.Record("and.i4.WithZero", And32(0x12345678, 0) == 0);
        TestTracker.Record("and.i4.Mask", And32(0x12345678, 0x0000FFFF) == 0x00005678);
        TestTracker.Record("and.i4.HighMask", And32(0x12345678, unchecked((int)0xFFFF0000)) == 0x12340000);
        TestTracker.Record("and.i4.Alternating", And32(unchecked((int)0xAAAAAAAA), 0x55555555) == 0);

        // int32 bit manipulation
        TestTracker.Record("and.i4.ExtractLowByte", And32(unchecked((int)0xDEADBEEF), 0xFF) == 0xEF);
        TestTracker.Record("and.i4.ExtractHighByte", And32(unchecked((int)0xDEADBEEF), unchecked((int)0xFF000000)) == unchecked((int)0xDE000000));
        TestTracker.Record("and.i4.TestBit0", (And32(5, 1) != 0) == true);
        TestTracker.Record("and.i4.TestBit1", (And32(5, 2) != 0) == false);
        TestTracker.Record("and.i4.TestBit2", (And32(5, 4) != 0) == true);
        TestTracker.Record("and.i4.ClearBit", And32(0x0F, ~(1 << 1)) == 0x0D);

        // int32 properties
        TestTracker.Record("and.i4.Commutative", And32(0xAB, 0xCD) == And32(0xCD, 0xAB));
        TestTracker.Record("and.i4.Idempotent", And32(0x1234, 0x1234) == 0x1234);
        TestTracker.Record("and.i4.ZeroAnnihilator", And32(0x12345678, 0) == 0);

        // int64 tests
        TestTracker.Record("and.i8.ZeroZero", And64(0L, 0L) == 0L);
        TestTracker.Record("and.i8.AllOnes", And64(-1L, -1L) == -1L);
        TestTracker.Record("and.i8.Basic", And64(0x123456789ABCDEF0L, 0x0000FFFF0000FFFFL) == 0x000056780000DEF0L);
        TestTracker.Record("and.i8.HighBits", And64(unchecked((long)0xFF00000000000000UL), unchecked((long)0xFF00000000000000UL)) == unchecked((long)0xFF00000000000000UL));
        TestTracker.Record("and.i8.LowBits", And64(0x00000000FFFFFFFFL, 0x00000000FFFFFFFFL) == 0x00000000FFFFFFFFL);
        TestTracker.Record("and.i8.HighLowZero", And64(unchecked((long)0xFFFFFFFF00000000UL), 0x00000000FFFFFFFFL) == 0L);
        TestTracker.Record("and.i8.Mask32", And64(0x123456789ABCDEF0L, 0x00000000FFFFFFFFL) == 0x000000009ABCDEF0L);
        TestTracker.Record("and.i8.Alternating", And64(unchecked((long)0xAAAAAAAAAAAAAAAAUL), 0x5555555555555555L) == 0L);
    }

    private static int And32(int a, int b) => a & b;
    private static long And64(long a, long b) => a & b;

    #endregion

    #region or (0x60)

    private static void TestOr()
    {
        // int32 basic tests
        TestTracker.Record("or.i4.ZeroZero", Or32(0, 0) == 0);
        TestTracker.Record("or.i4.ZeroX", Or32(0, 0x12345678) == 0x12345678);
        TestTracker.Record("or.i4.XZero", Or32(0x12345678, 0) == 0x12345678);
        TestTracker.Record("or.i4.AllOnesZero", Or32(unchecked((int)0xFFFFFFFF), 0) == unchecked((int)0xFFFFFFFF));
        TestTracker.Record("or.i4.ZeroAllOnes", Or32(0, unchecked((int)0xFFFFFFFF)) == unchecked((int)0xFFFFFFFF));
        TestTracker.Record("or.i4.AllOnesDominates", Or32(unchecked((int)0xFFFFFFFF), 0x12345678) == unchecked((int)0xFFFFFFFF));
        TestTracker.Record("or.i4.NoCommon", Or32(unchecked((int)0x0F0F0F0F), unchecked((int)0xF0F0F0F0)) == unchecked((int)0xFFFFFFFF));
        TestTracker.Record("or.i4.Combine", Or32(0xFF00, 0x00FF) == 0xFFFF);
        TestTracker.Record("or.i4.Identity", Or32(0x12345678, 0x12345678) == 0x12345678);
        TestTracker.Record("or.i4.Alternating", Or32(unchecked((int)0xAAAAAAAA), 0x55555555) == unchecked((int)0xFFFFFFFF));

        // int32 bit manipulation
        TestTracker.Record("or.i4.SetBit0", Or32(0, 1) == 1);
        TestTracker.Record("or.i4.SetBit7", Or32(0, 128) == 128);
        TestTracker.Record("or.i4.SetBit31", Or32(0, unchecked((int)0x80000000)) == unchecked((int)0x80000000));
        TestTracker.Record("or.i4.CombineFlags", Or32(0x01, 0x04) == 0x05);
        TestTracker.Record("or.i4.MergeBytes", Or32(0xAB00, 0x00CD) == 0xABCD);

        // int32 properties
        TestTracker.Record("or.i4.Commutative", Or32(0xAB, 0xCD) == Or32(0xCD, 0xAB));
        TestTracker.Record("or.i4.Idempotent", Or32(0x1234, 0x1234) == 0x1234);
        TestTracker.Record("or.i4.ZeroIdentity", Or32(0x12345678, 0) == 0x12345678);
        TestTracker.Record("or.i4.AllOnesDominates2", Or32(0x12345678, unchecked((int)0xFFFFFFFF)) == unchecked((int)0xFFFFFFFF));

        // int64 tests
        TestTracker.Record("or.i8.ZeroZero", Or64(0L, 0L) == 0L);
        TestTracker.Record("or.i8.Basic", Or64(unchecked((long)0x00FF00FF00FF00FFUL), unchecked((long)0xFF00FF00FF00FF00UL)) == -1L);
        TestTracker.Record("or.i8.HighLow", Or64(unchecked((long)0xFFFFFFFF00000000UL), 0x00000000FFFFFFFFL) == -1L);
        TestTracker.Record("or.i8.Combine", Or64(0x123400000000L, 0x00005678L) == 0x123400005678L);
        TestTracker.Record("or.i8.Identity", Or64(0x123456789ABCDEF0L, 0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("or.i8.Alternating", Or64(unchecked((long)0xAAAAAAAAAAAAAAAAUL), 0x5555555555555555L) == -1L);
    }

    private static int Or32(int a, int b) => a | b;
    private static long Or64(long a, long b) => a | b;

    #endregion

    #region xor (0x61)

    private static void TestXor()
    {
        // int32 basic tests
        TestTracker.Record("xor.i4.ZeroZero", Xor32(0, 0) == 0);
        TestTracker.Record("xor.i4.ZeroX", Xor32(0, 0x12345678) == 0x12345678);
        TestTracker.Record("xor.i4.XZero", Xor32(0x12345678, 0) == 0x12345678);
        TestTracker.Record("xor.i4.SameValue", Xor32(0x12345678, 0x12345678) == 0);
        TestTracker.Record("xor.i4.AllOnesAllOnes", Xor32(unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF)) == 0);
        TestTracker.Record("xor.i4.AllOnesNot", Xor32(unchecked((int)0xFFFFFFFF), unchecked((int)0xAAAAAAAA)) == 0x55555555);
        TestTracker.Record("xor.i4.NoCommon", Xor32(unchecked((int)0x0F0F0F0F), unchecked((int)0xF0F0F0F0)) == unchecked((int)0xFFFFFFFF));
        TestTracker.Record("xor.i4.Alternating", Xor32(unchecked((int)0xAAAAAAAA), 0x55555555) == unchecked((int)0xFFFFFFFF));

        // int32 bit manipulation
        TestTracker.Record("xor.i4.ToggleBit0", Xor32(0x00, 0x01) == 0x01);
        TestTracker.Record("xor.i4.ToggleBit0On", Xor32(0x01, 0x01) == 0x00);
        TestTracker.Record("xor.i4.ToggleBit7", Xor32(0x00, 0x80) == 0x80);
        TestTracker.Record("xor.i4.FlipAll", Xor32(0x12345678, unchecked((int)0xFFFFFFFF)) == unchecked((int)0xEDCBA987));

        // int32 properties
        TestTracker.Record("xor.i4.Commutative", Xor32(0xAB, 0xCD) == Xor32(0xCD, 0xAB));
        TestTracker.Record("xor.i4.SelfInverse", Xor32(0x1234, 0x1234) == 0);
        TestTracker.Record("xor.i4.ZeroIdentity", Xor32(0x12345678, 0) == 0x12345678);
        TestTracker.Record("xor.i4.DoubleXor", Xor32(Xor32(0x1234, 0x5678), 0x5678) == 0x1234);

        // XOR equals NOT when XOR with all-ones
        TestTracker.Record("xor.i4.EqualsNot", Xor32(0x12345678, unchecked((int)0xFFFFFFFF)) == ~0x12345678);

        // int64 tests
        TestTracker.Record("xor.i8.ZeroZero", Xor64(0L, 0L) == 0L);
        TestTracker.Record("xor.i8.Basic", Xor64(0x123456789ABCDEF0L, 0x123456789ABCDEF0L) == 0L);
        TestTracker.Record("xor.i8.ZeroIdentity", Xor64(0x123456789ABCDEF0L, 0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("xor.i8.DoubleXor", Xor64(Xor64(0x123456789ABCDEF0L, unchecked((long)0xFEDCBA9876543210UL)), unchecked((long)0xFEDCBA9876543210UL)) == 0x123456789ABCDEF0L);
        TestTracker.Record("xor.i8.AllOnesNot", Xor64(0x123456789ABCDEF0L, -1L) == ~0x123456789ABCDEF0L);
        TestTracker.Record("xor.i8.Alternating", Xor64(unchecked((long)0xAAAAAAAAAAAAAAAAUL), unchecked((long)0x5555555555555555UL)) == -1L);
    }

    private static int Xor32(int a, int b) => a ^ b;
    private static long Xor64(long a, long b) => a ^ b;

    #endregion

    #region not (0x66)

    private static void TestNot()
    {
        // int32 basic tests
        TestTracker.Record("not.i4.Zero", Not32(0) == -1);
        TestTracker.Record("not.i4.AllOnes", Not32(-1) == 0);
        TestTracker.Record("not.i4.One", Not32(1) == -2);
        TestTracker.Record("not.i4.Pattern0F", Not32(unchecked((int)0x0F0F0F0F)) == unchecked((int)0xF0F0F0F0));
        TestTracker.Record("not.i4.PatternF0", Not32(unchecked((int)0xF0F0F0F0)) == unchecked((int)0x0F0F0F0F));
        TestTracker.Record("not.i4.PatternAA", Not32(unchecked((int)0xAAAAAAAA)) == 0x55555555);
        TestTracker.Record("not.i4.Pattern55", Not32(0x55555555) == unchecked((int)0xAAAAAAAA));
        TestTracker.Record("not.i4.MaxValue", Not32(int.MaxValue) == int.MinValue);
        TestTracker.Record("not.i4.MinValue", Not32(int.MinValue) == int.MaxValue);

        // int32 properties
        TestTracker.Record("not.i4.DoubleNot", Not32(Not32(0x12345678)) == 0x12345678);
        TestTracker.Record("not.i4.RelToNeg", Not32(5) == -5 - 1); // ~x = -x - 1
        TestTracker.Record("not.i4.XorEquiv", Not32(0x1234) == Xor32(0x1234, -1)); // ~x = x ^ -1

        // int32 bit manipulation
        TestTracker.Record("not.i4.CreateMask", Not32(0) == -1); // All bits set
        TestTracker.Record("not.i4.InvertMask", Not32(0x00FF) == unchecked((int)0xFFFFFF00));
        TestTracker.Record("not.i4.ClearBitsAnd", And32(0xFF, Not32(0x0F)) == 0xF0); // x & ~mask

        // int64 tests
        TestTracker.Record("not.i8.Zero", Not64(0L) == -1L);
        TestTracker.Record("not.i8.AllOnes", Not64(-1L) == 0L);
        TestTracker.Record("not.i8.One", Not64(1L) == -2L);
        TestTracker.Record("not.i8.Pattern", Not64(unchecked((long)0x0F0F0F0F0F0F0F0FUL)) == unchecked((long)0xF0F0F0F0F0F0F0F0UL));
        TestTracker.Record("not.i8.DoubleNot", Not64(Not64(0x123456789ABCDEF0L)) == 0x123456789ABCDEF0L);
        TestTracker.Record("not.i8.MaxValue", Not64(long.MaxValue) == long.MinValue);
        TestTracker.Record("not.i8.MinValue", Not64(long.MinValue) == long.MaxValue);
    }

    private static int Not32(int a) => ~a;
    private static long Not64(long a) => ~a;

    #endregion

    #region shl (0x62)

    private static void TestShl()
    {
        // int32 basic tests
        TestTracker.Record("shl.i4.By0", Shl32(1, 0) == 1);
        TestTracker.Record("shl.i4.By1", Shl32(1, 1) == 2);
        TestTracker.Record("shl.i4.By2", Shl32(1, 2) == 4);
        TestTracker.Record("shl.i4.By4", Shl32(1, 4) == 16);
        TestTracker.Record("shl.i4.By8", Shl32(1, 8) == 256);
        TestTracker.Record("shl.i4.By16", Shl32(1, 16) == 65536);
        TestTracker.Record("shl.i4.By31", Shl32(1, 31) == int.MinValue);
        TestTracker.Record("shl.i4.ShiftOut", Shl32(unchecked((int)0x80000000), 1) == 0);

        // Shift amount masking (mod 32 for int32)
        TestTracker.Record("shl.i4.By32Masked", Shl32(1, 32) == 1); // 32 & 31 = 0, no shift
        TestTracker.Record("shl.i4.By33Masked", Shl32(1, 33) == 2); // 33 & 31 = 1

        // int32 various values
        TestTracker.Record("shl.i4.ZeroByN", Shl32(0, 5) == 0);
        TestTracker.Record("shl.i4.AllOnesBy1", Shl32(-1, 1) == -2);
        TestTracker.Record("shl.i4.AllOnesBy4", Shl32(-1, 4) == unchecked((int)0xFFFFFFF0));
        TestTracker.Record("shl.i4.PatternBy4", Shl32(0x12345678, 4) == 0x23456780);
        TestTracker.Record("shl.i4.PatternBy8", Shl32(0x12345678, 8) == 0x34567800);

        // Multiply equivalence
        TestTracker.Record("shl.i4.Mul2", Shl32(5, 1) == 5 * 2);
        TestTracker.Record("shl.i4.Mul4", Shl32(5, 2) == 5 * 4);
        TestTracker.Record("shl.i4.Mul8", Shl32(5, 3) == 5 * 8);
        TestTracker.Record("shl.i4.Mul256", Shl32(100, 8) == 100 * 256);

        // int64 tests
        TestTracker.Record("shl.i8.By0", Shl64(1L, 0) == 1L);
        TestTracker.Record("shl.i8.By1", Shl64(1L, 1) == 2L);
        TestTracker.Record("shl.i8.By32", Shl64(1L, 32) == 0x100000000L);
        TestTracker.Record("shl.i8.By63", Shl64(1L, 63) == long.MinValue);
        TestTracker.Record("shl.i8.By64Masked", Shl64(1L, 64) == 1L); // 64 & 63 = 0
        TestTracker.Record("shl.i8.By65Masked", Shl64(1L, 65) == 2L); // 65 & 63 = 1
        TestTracker.Record("shl.i8.Pattern", Shl64(0x123456789ABCDEF0L, 4) == 0x23456789ABCDEF00L);
        TestTracker.Record("shl.i8.AllOnesBy1", Shl64(-1L, 1) == -2L);
        TestTracker.Record("shl.i8.ShiftToHigh", Shl64(0xFFL, 56) == unchecked((long)0xFF00000000000000UL));
    }

    private static int Shl32(int a, int shift) => a << shift;
    private static long Shl64(long a, int shift) => a << shift;

    #endregion

    #region shr (0x63) - signed/arithmetic shift right

    private static void TestShr()
    {
        // int32 positive values (zero-fill from left)
        TestTracker.Record("shr.i4.PosBy0", Shr32(4, 0) == 4);
        TestTracker.Record("shr.i4.PosBy1", Shr32(4, 1) == 2);
        TestTracker.Record("shr.i4.PosBy2", Shr32(4, 2) == 1);
        TestTracker.Record("shr.i4.PosBy3", Shr32(4, 3) == 0);
        TestTracker.Record("shr.i4.16By2", Shr32(16, 2) == 4);
        TestTracker.Record("shr.i4.100By4", Shr32(100, 4) == 6);
        TestTracker.Record("shr.i4.MaxBy1", Shr32(int.MaxValue, 1) == 0x3FFFFFFF);

        // int32 negative values (sign-fill from left)
        TestTracker.Record("shr.i4.NegOneBy1", Shr32(-1, 1) == -1); // All ones stays all ones
        TestTracker.Record("shr.i4.NegOneBy31", Shr32(-1, 31) == -1);
        TestTracker.Record("shr.i4.NegTwoBy1", Shr32(-2, 1) == -1);
        TestTracker.Record("shr.i4.NegFourBy1", Shr32(-4, 1) == -2);
        TestTracker.Record("shr.i4.NegFourBy2", Shr32(-4, 2) == -1);
        TestTracker.Record("shr.i4.MinBy1", Shr32(int.MinValue, 1) == unchecked((int)0xC0000000));
        TestTracker.Record("shr.i4.MinBy31", Shr32(int.MinValue, 31) == -1);
        TestTracker.Record("shr.i4.Neg128By7", Shr32(-128, 7) == -1);

        // Shift amount masking (mod 32 for int32)
        TestTracker.Record("shr.i4.By32Masked", Shr32(8, 32) == 8); // 32 & 31 = 0
        TestTracker.Record("shr.i4.By33Masked", Shr32(8, 33) == 4); // 33 & 31 = 1

        // Divide equivalence (for positive numbers)
        TestTracker.Record("shr.i4.Div2", Shr32(10, 1) == 10 / 2);
        TestTracker.Record("shr.i4.Div4", Shr32(100, 2) == 100 / 4);
        TestTracker.Record("shr.i4.Div8", Shr32(100, 3) == 100 / 8);

        // int64 positive
        TestTracker.Record("shr.i8.PosBy1", Shr64(16L, 1) == 8L);
        TestTracker.Record("shr.i8.PosBy32", Shr64(0x100000000L, 32) == 1L);
        TestTracker.Record("shr.i8.MaxBy1", Shr64(long.MaxValue, 1) == 0x3FFFFFFFFFFFFFFFL);

        // int64 negative (sign extension)
        TestTracker.Record("shr.i8.NegOneBy1", Shr64(-1L, 1) == -1L);
        TestTracker.Record("shr.i8.NegTwoBy1", Shr64(-2L, 1) == -1L);
        TestTracker.Record("shr.i8.NegFourBy1", Shr64(-4L, 1) == -2L);
        TestTracker.Record("shr.i8.MinBy1", Shr64(long.MinValue, 1) == unchecked((long)0xC000000000000000UL));
        TestTracker.Record("shr.i8.MinBy63", Shr64(long.MinValue, 63) == -1L);

        // Shift amount masking (mod 64 for int64)
        TestTracker.Record("shr.i8.By64Masked", Shr64(8L, 64) == 8L); // 64 & 63 = 0
    }

    private static int Shr32(int a, int shift) => a >> shift;
    private static long Shr64(long a, int shift) => a >> shift;

    #endregion

    #region shr.un (0x64) - unsigned/logical shift right

    private static void TestShrUn()
    {
        // uint32 basic tests (zero-fill from left, no sign extension)
        TestTracker.Record("shr.un.i4.By0", ShrUn32(4u, 0) == 4u);
        TestTracker.Record("shr.un.i4.By1", ShrUn32(4u, 1) == 2u);
        TestTracker.Record("shr.un.i4.By2", ShrUn32(4u, 2) == 1u);
        TestTracker.Record("shr.un.i4.16By2", ShrUn32(16u, 2) == 4u);
        TestTracker.Record("shr.un.i4.HighBitBy1", ShrUn32(0x80000000u, 1) == 0x40000000u);
        TestTracker.Record("shr.un.i4.HighBitBy31", ShrUn32(0x80000000u, 31) == 1u);
        TestTracker.Record("shr.un.i4.AllOnesBy1", ShrUn32(0xFFFFFFFFu, 1) == 0x7FFFFFFFu);
        TestTracker.Record("shr.un.i4.AllOnesBy4", ShrUn32(0xFFFFFFFFu, 4) == 0x0FFFFFFFu);
        TestTracker.Record("shr.un.i4.AllOnesBy31", ShrUn32(0xFFFFFFFFu, 31) == 1u);
        TestTracker.Record("shr.un.i4.PatternBy4", ShrUn32(0xDEADBEEFu, 4) == 0x0DEADBEEu);
        TestTracker.Record("shr.un.i4.PatternBy8", ShrUn32(0xDEADBEEFu, 8) == 0x00DEADBEu);

        // Difference from signed shr: high bit treated as data, not sign
        TestTracker.Record("shr.un.i4.NoSignExt", ShrUn32(0xF0000000u, 4) == 0x0F000000u);

        // Shift amount masking (mod 32)
        TestTracker.Record("shr.un.i4.By32Masked", ShrUn32(8u, 32) == 8u);
        TestTracker.Record("shr.un.i4.By33Masked", ShrUn32(8u, 33) == 4u);

        // Division equivalence
        TestTracker.Record("shr.un.i4.Div2", ShrUn32(100u, 1) == 100u / 2u);
        TestTracker.Record("shr.un.i4.Div256", ShrUn32(65536u, 8) == 256u);

        // uint64 tests
        TestTracker.Record("shr.un.i8.By1", ShrUn64(16UL, 1) == 8UL);
        TestTracker.Record("shr.un.i8.By32", ShrUn64(0x100000000UL, 32) == 1UL);
        TestTracker.Record("shr.un.i8.HighBitBy1", ShrUn64(0x8000000000000000UL, 1) == 0x4000000000000000UL);
        TestTracker.Record("shr.un.i8.HighBitBy63", ShrUn64(0x8000000000000000UL, 63) == 1UL);
        TestTracker.Record("shr.un.i8.AllOnesBy1", ShrUn64(0xFFFFFFFFFFFFFFFFUL, 1) == 0x7FFFFFFFFFFFFFFFUL);
        TestTracker.Record("shr.un.i8.AllOnesBy32", ShrUn64(0xFFFFFFFFFFFFFFFFUL, 32) == 0x00000000FFFFFFFFUL);
        TestTracker.Record("shr.un.i8.AllOnesBy63", ShrUn64(0xFFFFFFFFFFFFFFFFUL, 63) == 1UL);
        TestTracker.Record("shr.un.i8.PatternBy4", ShrUn64(0xDEADBEEFCAFEBABEUL, 4) == 0x0DEADBEEFCAFEBABUL);

        // Shift amount masking (mod 64)
        TestTracker.Record("shr.un.i8.By64Masked", ShrUn64(8UL, 64) == 8UL);
        TestTracker.Record("shr.un.i8.By65Masked", ShrUn64(8UL, 65) == 4UL);

        // Demonstrate unsigned vs signed difference
        // Signed: 0x80000000 >> 1 = 0xC0000000 (sign extends)
        // Unsigned: 0x80000000 >> 1 = 0x40000000 (zero fills)
        TestTracker.Record("shr.un.i4.VsSignedDiff", ShrUn32(0x80000000u, 1) != (uint)Shr32(unchecked((int)0x80000000), 1));
    }

    private static uint ShrUn32(uint a, int shift) => a >> shift;
    private static ulong ShrUn64(ulong a, int shift) => a >> shift;

    #endregion

    #region Small Type Bitwise Tests

    public static void RunSmallTypeTests()
    {
        TestAndSmallTypes();
        TestOrSmallTypes();
        TestXorSmallTypes();
        TestNotSmallTypes();
        TestShlSmallTypes();
        TestShrSmallTypes();
    }

    private static void TestAndSmallTypes()
    {
        // Byte AND
        TestTracker.Record("and.byte.ZeroZero", AndByte(0, 0) == 0);
        TestTracker.Record("and.byte.AllOnes", AndByte(0xFF, 0xFF) == 0xFF);
        TestTracker.Record("and.byte.Mask", AndByte(0xAB, 0x0F) == 0x0B);
        TestTracker.Record("and.byte.NoCommon", AndByte(0xF0, 0x0F) == 0);
        TestTracker.Record("and.byte.Identity", AndByte(0xAB, 0xFF) == 0xAB);

        // SByte AND
        TestTracker.Record("and.sbyte.Basic", AndSByte(-1, -1) == -1);
        TestTracker.Record("and.sbyte.WithZero", AndSByte(-1, 0) == 0);
        TestTracker.Record("and.sbyte.Mask", AndSByte(0x7F, 0x0F) == 0x0F);
        TestTracker.Record("and.sbyte.Negative", AndSByte(-128, 127) == 0);

        // Short AND
        TestTracker.Record("and.short.ZeroZero", AndShort(0, 0) == 0);
        TestTracker.Record("and.short.AllOnes", AndShort(-1, -1) == -1);
        TestTracker.Record("and.short.Mask", AndShort(0x1234, 0x00FF) == 0x0034);
        TestTracker.Record("and.short.HighMask", AndShort(0x1234, unchecked((short)0xFF00)) == 0x1200);

        // UShort AND
        TestTracker.Record("and.ushort.Basic", AndUShort(0xABCD, 0xF0F0) == 0xA0C0);
        TestTracker.Record("and.ushort.Max", AndUShort(0xFFFF, 0xFFFF) == 0xFFFF);
        TestTracker.Record("and.ushort.Mask", AndUShort(0xFFFF, 0x00FF) == 0x00FF);
    }

    private static byte AndByte(byte a, byte b) => (byte)(a & b);
    private static sbyte AndSByte(sbyte a, sbyte b) => (sbyte)(a & b);
    private static short AndShort(short a, short b) => (short)(a & b);
    private static ushort AndUShort(ushort a, ushort b) => (ushort)(a & b);

    private static void TestOrSmallTypes()
    {
        // Byte OR
        TestTracker.Record("or.byte.ZeroZero", OrByte(0, 0) == 0);
        TestTracker.Record("or.byte.Combine", OrByte(0xF0, 0x0F) == 0xFF);
        TestTracker.Record("or.byte.Identity", OrByte(0xAB, 0) == 0xAB);
        TestTracker.Record("or.byte.SetBits", OrByte(0x00, 0x80) == 0x80);

        // SByte OR
        TestTracker.Record("or.sbyte.Basic", OrSByte(0x0F, 0x70) == 0x7F);
        TestTracker.Record("or.sbyte.Negative", OrSByte(-128, 0) == -128);
        TestTracker.Record("or.sbyte.AllOnes", OrSByte(-1, 0) == -1);

        // Short OR
        TestTracker.Record("or.short.ZeroZero", OrShort(0, 0) == 0);
        TestTracker.Record("or.short.Combine", OrShort(0x00FF, unchecked((short)0xFF00)) == -1);
        TestTracker.Record("or.short.Identity", OrShort(0x1234, 0) == 0x1234);

        // UShort OR
        TestTracker.Record("or.ushort.Combine", OrUShort(0x00FF, 0xFF00) == 0xFFFF);
        TestTracker.Record("or.ushort.SetBits", OrUShort(0x0000, 0x8000) == 0x8000);
        TestTracker.Record("or.ushort.Identity", OrUShort(0xABCD, 0) == 0xABCD);
    }

    private static byte OrByte(byte a, byte b) => (byte)(a | b);
    private static sbyte OrSByte(sbyte a, sbyte b) => (sbyte)(a | b);
    private static short OrShort(short a, short b) => (short)(a | b);
    private static ushort OrUShort(ushort a, ushort b) => (ushort)(a | b);

    private static void TestXorSmallTypes()
    {
        // Byte XOR
        TestTracker.Record("xor.byte.ZeroZero", XorByte(0, 0) == 0);
        TestTracker.Record("xor.byte.SelfZero", XorByte(0xAB, 0xAB) == 0);
        TestTracker.Record("xor.byte.Toggle", XorByte(0xAA, 0xFF) == 0x55);
        TestTracker.Record("xor.byte.Identity", XorByte(0xAB, 0) == 0xAB);

        // SByte XOR
        TestTracker.Record("xor.sbyte.SelfZero", XorSByte(-1, -1) == 0);
        TestTracker.Record("xor.sbyte.Flip", XorSByte(0x55, -1) == unchecked((sbyte)0xAA));
        TestTracker.Record("xor.sbyte.Identity", XorSByte(-128, 0) == -128);

        // Short XOR
        TestTracker.Record("xor.short.ZeroZero", XorShort(0, 0) == 0);
        TestTracker.Record("xor.short.SelfZero", XorShort(0x1234, 0x1234) == 0);
        TestTracker.Record("xor.short.Toggle", XorShort(0x00FF, -1) == unchecked((short)0xFF00));

        // UShort XOR
        TestTracker.Record("xor.ushort.SelfZero", XorUShort(0xABCD, 0xABCD) == 0);
        TestTracker.Record("xor.ushort.Toggle", XorUShort(0x5555, 0xFFFF) == 0xAAAA);
        TestTracker.Record("xor.ushort.Identity", XorUShort(0xABCD, 0) == 0xABCD);
    }

    private static byte XorByte(byte a, byte b) => (byte)(a ^ b);
    private static sbyte XorSByte(sbyte a, sbyte b) => (sbyte)(a ^ b);
    private static short XorShort(short a, short b) => (short)(a ^ b);
    private static ushort XorUShort(ushort a, ushort b) => (ushort)(a ^ b);

    private static void TestNotSmallTypes()
    {
        // Byte NOT
        TestTracker.Record("not.byte.Zero", NotByte(0) == 0xFF);
        TestTracker.Record("not.byte.AllOnes", NotByte(0xFF) == 0);
        TestTracker.Record("not.byte.Pattern", NotByte(0xAA) == 0x55);
        TestTracker.Record("not.byte.DoubleNot", NotByte(NotByte(0xAB)) == 0xAB);

        // SByte NOT
        TestTracker.Record("not.sbyte.Zero", NotSByte(0) == -1);
        TestTracker.Record("not.sbyte.AllOnes", NotSByte(-1) == 0);
        TestTracker.Record("not.sbyte.Max", NotSByte(127) == -128);
        TestTracker.Record("not.sbyte.Min", NotSByte(-128) == 127);

        // Short NOT
        TestTracker.Record("not.short.Zero", NotShort(0) == -1);
        TestTracker.Record("not.short.AllOnes", NotShort(-1) == 0);
        TestTracker.Record("not.short.Max", NotShort(32767) == -32768);
        TestTracker.Record("not.short.Min", NotShort(-32768) == 32767);

        // UShort NOT
        TestTracker.Record("not.ushort.Zero", NotUShort(0) == 0xFFFF);
        TestTracker.Record("not.ushort.AllOnes", NotUShort(0xFFFF) == 0);
        TestTracker.Record("not.ushort.Pattern", NotUShort(0xAAAA) == 0x5555);
    }

    private static byte NotByte(byte a) => (byte)(~a);
    private static sbyte NotSByte(sbyte a) => (sbyte)(~a);
    private static short NotShort(short a) => (short)(~a);
    private static ushort NotUShort(ushort a) => (ushort)(~a);

    private static void TestShlSmallTypes()
    {
        // Byte SHL
        TestTracker.Record("shl.byte.By0", ShlByte(1, 0) == 1);
        TestTracker.Record("shl.byte.By1", ShlByte(1, 1) == 2);
        TestTracker.Record("shl.byte.By7", ShlByte(1, 7) == 128);
        TestTracker.Record("shl.byte.Overflow", ShlByte(1, 8) == 0);
        TestTracker.Record("shl.byte.Pattern", ShlByte(0x12, 4) == 0x20);

        // SByte SHL
        TestTracker.Record("shl.sbyte.By0", ShlSByte(1, 0) == 1);
        TestTracker.Record("shl.sbyte.By1", ShlSByte(1, 1) == 2);
        TestTracker.Record("shl.sbyte.By6", ShlSByte(1, 6) == 64);
        TestTracker.Record("shl.sbyte.ToNeg", ShlSByte(1, 7) == -128);

        // Short SHL
        TestTracker.Record("shl.short.By0", ShlShort(1, 0) == 1);
        TestTracker.Record("shl.short.By8", ShlShort(1, 8) == 256);
        TestTracker.Record("shl.short.By15", ShlShort(1, 15) == -32768);
        TestTracker.Record("shl.short.Pattern", ShlShort(0x12, 8) == 0x1200);

        // UShort SHL
        TestTracker.Record("shl.ushort.By0", ShlUShort(1, 0) == 1);
        TestTracker.Record("shl.ushort.By8", ShlUShort(1, 8) == 256);
        TestTracker.Record("shl.ushort.By15", ShlUShort(1, 15) == 0x8000);
        TestTracker.Record("shl.ushort.Overflow", ShlUShort(1, 16) == 0);
    }

    private static byte ShlByte(byte a, int shift) => (byte)(a << shift);
    private static sbyte ShlSByte(sbyte a, int shift) => (sbyte)(a << shift);
    private static short ShlShort(short a, int shift) => (short)(a << shift);
    private static ushort ShlUShort(ushort a, int shift) => (ushort)(a << shift);

    private static void TestShrSmallTypes()
    {
        // Byte SHR (unsigned, zero-fills)
        TestTracker.Record("shr.byte.By0", ShrByte(128, 0) == 128);
        TestTracker.Record("shr.byte.By1", ShrByte(128, 1) == 64);
        TestTracker.Record("shr.byte.By7", ShrByte(128, 7) == 1);
        TestTracker.Record("shr.byte.ToZero", ShrByte(128, 8) == 0);
        TestTracker.Record("shr.byte.Pattern", ShrByte(0xAB, 4) == 0x0A);

        // SByte SHR (signed, sign-extends)
        TestTracker.Record("shr.sbyte.Positive", ShrSByte(64, 2) == 16);
        TestTracker.Record("shr.sbyte.Negative", ShrSByte(-128, 1) == -64);
        TestTracker.Record("shr.sbyte.NegBy7", ShrSByte(-128, 7) == -1);
        TestTracker.Record("shr.sbyte.NegOne", ShrSByte(-1, 5) == -1);

        // Short SHR (signed, sign-extends)
        TestTracker.Record("shr.short.Positive", ShrShort(1024, 2) == 256);
        TestTracker.Record("shr.short.Negative", ShrShort(-32768, 1) == -16384);
        TestTracker.Record("shr.short.NegBy15", ShrShort(-32768, 15) == -1);
        TestTracker.Record("shr.short.NegOne", ShrShort(-1, 10) == -1);

        // UShort SHR (unsigned, zero-fills)
        TestTracker.Record("shr.ushort.Basic", ShrUShort(0x8000, 1) == 0x4000);
        TestTracker.Record("shr.ushort.By8", ShrUShort(0xABCD, 8) == 0x00AB);
        TestTracker.Record("shr.ushort.By15", ShrUShort(0x8000, 15) == 1);
        TestTracker.Record("shr.ushort.ToZero", ShrUShort(0x8000, 16) == 0);
    }

    private static byte ShrByte(byte a, int shift) => (byte)(a >> shift);
    private static sbyte ShrSByte(sbyte a, int shift) => (sbyte)(a >> shift);
    private static short ShrShort(short a, int shift) => (short)(a >> shift);
    private static ushort ShrUShort(ushort a, int shift) => (ushort)(a >> shift);

    #endregion
}
