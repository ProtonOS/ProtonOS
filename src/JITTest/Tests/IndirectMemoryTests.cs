// JITTest - Indirect Memory Tests
// Sections 9-10: ldind.* (0x46-0x50), stind.* (0x51-0x57)

namespace JITTest;

/// <summary>
/// Tests for indirect load and store instructions (ECMA-335 Section III)
/// Tests pointer dereferencing operations for all primitive types
/// </summary>
public static class IndirectMemoryTests
{
    // Test struct for field access
    private struct TestStruct
    {
        public sbyte SByteField;
        public byte ByteField;
        public short ShortField;
        public ushort UShortField;
        public int IntField;
        public uint UIntField;
        public long LongField;
        public float FloatField;
        public double DoubleField;
    }

    public static void RunAll()
    {
        // ldind tests
        TestLdindI1();
        TestLdindU1();
        TestLdindI2();
        TestLdindU2();
        TestLdindI4();
        TestLdindU4();
        TestLdindI8();
        TestLdindI();
        TestLdindR4();
        TestLdindR8();
        TestLdindRef();

        // stind tests
        TestStindI1();
        TestStindI2();
        TestStindI4();
        TestStindI8();
        TestStindI();
        TestStindR4();
        TestStindR8();
        TestStindRef();

        // Round-trip tests
        TestRoundTrip();

        // Array element access
        TestArrayElementAccess();

        // ldloc sign-extension after stind
        TestLdlocSignExtendSByte();
        TestLdlocSignExtendShort();

        // Additional indirect memory tests
        RunAdditionalTests();
    }

    #region ldind.i1 Tests

    private static unsafe void TestLdindI1()
    {
        // Basic values
        sbyte value = 42;
        sbyte* ptr = &value;
        TestTracker.Record("ldind.i1.Positive", *ptr == 42);

        value = -42;
        TestTracker.Record("ldind.i1.Negative", *ptr == -42);

        value = 0;
        TestTracker.Record("ldind.i1.Zero", *ptr == 0);

        // Boundary values
        value = sbyte.MaxValue;  // 127
        TestTracker.Record("ldind.i1.Max", *ptr == 127);

        value = sbyte.MinValue;  // -128
        TestTracker.Record("ldind.i1.Min", *ptr == -128);

        value = -1;
        TestTracker.Record("ldind.i1.Neg1", *ptr == -1);

        // Multiple loads from same address
        value = 100;
        TestTracker.Record("ldind.i1.MultiLoad", *ptr == 100 && *ptr == 100);

        // Load after modification
        value = 10;
        TestTracker.Record("ldind.i1.BeforeModify", *ptr == 10);
        value = 20;
        TestTracker.Record("ldind.i1.AfterModify", *ptr == 20);

        // Sign extension verification (high bit set)
        value = unchecked((sbyte)0x80);  // -128
        int extended = *ptr;
        TestTracker.Record("ldind.i1.SignExtend", extended == -128);
    }

    #endregion

    #region ldind.u1 Tests

    private static unsafe void TestLdindU1()
    {
        byte value = 200;
        byte* ptr = &value;
        TestTracker.Record("ldind.u1.Basic", *ptr == 200);

        value = 0;
        TestTracker.Record("ldind.u1.Zero", *ptr == 0);

        value = 127;
        TestTracker.Record("ldind.u1.127", *ptr == 127);

        value = 128;  // 0x80 - would be negative if sign-extended
        TestTracker.Record("ldind.u1.128", *ptr == 128);

        value = byte.MaxValue;  // 255
        TestTracker.Record("ldind.u1.Max", *ptr == 255);

        // Zero extension verification
        value = 0x80;  // 128
        int extended = *ptr;
        TestTracker.Record("ldind.u1.ZeroExtend", extended == 128 && extended > 0);

        value = 0xFF;  // 255
        extended = *ptr;
        TestTracker.Record("ldind.u1.ZeroExtendFF", extended == 255 && extended > 0);
    }

    #endregion

    #region ldind.i2 Tests

    private static unsafe void TestLdindI2()
    {
        short value = 1000;
        short* ptr = &value;
        TestTracker.Record("ldind.i2.Positive", *ptr == 1000);

        value = -1000;
        TestTracker.Record("ldind.i2.Negative", *ptr == -1000);

        value = 0;
        TestTracker.Record("ldind.i2.Zero", *ptr == 0);

        value = short.MaxValue;  // 32767
        TestTracker.Record("ldind.i2.Max", *ptr == 32767);

        value = short.MinValue;  // -32768
        TestTracker.Record("ldind.i2.Min", *ptr == -32768);

        value = -1;
        TestTracker.Record("ldind.i2.Neg1", *ptr == -1);

        // Sign extension verification
        value = unchecked((short)0x8000);  // -32768
        int extended = *ptr;
        TestTracker.Record("ldind.i2.SignExtend", extended == -32768);
    }

    #endregion

    #region ldind.u2 Tests

    private static unsafe void TestLdindU2()
    {
        ushort value = 50000;
        ushort* ptr = &value;
        TestTracker.Record("ldind.u2.Basic", *ptr == 50000);

        value = 0;
        TestTracker.Record("ldind.u2.Zero", *ptr == 0);

        value = 32767;
        TestTracker.Record("ldind.u2.32767", *ptr == 32767);

        value = 32768;  // 0x8000 - would be negative if sign-extended
        TestTracker.Record("ldind.u2.32768", *ptr == 32768);

        value = ushort.MaxValue;  // 65535
        TestTracker.Record("ldind.u2.Max", *ptr == 65535);

        // Zero extension verification
        value = 0x8000;  // 32768
        int extended = *ptr;
        TestTracker.Record("ldind.u2.ZeroExtend", extended == 32768 && extended > 0);

        value = 0xFFFF;  // 65535
        extended = *ptr;
        TestTracker.Record("ldind.u2.ZeroExtendFFFF", extended == 65535 && extended > 0);

        // Char as uint16
        char c = 'Z';
        ushort* charPtr = (ushort*)&c;
        TestTracker.Record("ldind.u2.Char", *charPtr == 90);
    }

    #endregion

    #region ldind.i4 Tests

    private static unsafe void TestLdindI4()
    {
        int value = 1000000;
        int* ptr = &value;
        TestTracker.Record("ldind.i4.Positive", *ptr == 1000000);

        value = -1000000;
        TestTracker.Record("ldind.i4.Negative", *ptr == -1000000);

        value = 0;
        TestTracker.Record("ldind.i4.Zero", *ptr == 0);

        value = 1;
        TestTracker.Record("ldind.i4.One", *ptr == 1);

        value = -1;
        TestTracker.Record("ldind.i4.Neg1", *ptr == -1);

        value = int.MaxValue;
        TestTracker.Record("ldind.i4.Max", *ptr == int.MaxValue);

        value = int.MinValue;
        TestTracker.Record("ldind.i4.Min", *ptr == int.MinValue);

        // Bit pattern tests
        value = unchecked((int)0xDEADBEEF);
        TestTracker.Record("ldind.i4.Pattern", *ptr == unchecked((int)0xDEADBEEF));
    }

    #endregion

    #region ldind.u4 Tests

    private static unsafe void TestLdindU4()
    {
        uint value = 3000000000;
        uint* ptr = &value;
        TestTracker.Record("ldind.u4.Basic", *ptr == 3000000000);

        value = 0;
        TestTracker.Record("ldind.u4.Zero", *ptr == 0);

        value = uint.MaxValue;
        TestTracker.Record("ldind.u4.Max", *ptr == uint.MaxValue);

        value = 0x80000000;  // High bit set
        TestTracker.Record("ldind.u4.HighBit", *ptr == 0x80000000);

        // Bit pattern preservation
        value = 0xDEADBEEF;
        TestTracker.Record("ldind.u4.Pattern", *ptr == 0xDEADBEEF);
    }

    #endregion

    #region ldind.i8 Tests

    private static unsafe void TestLdindI8()
    {
        long value = 1000000000000L;
        long* ptr = &value;
        TestTracker.Record("ldind.i8.Positive", *ptr == 1000000000000L);

        value = -1000000000000L;
        TestTracker.Record("ldind.i8.Negative", *ptr == -1000000000000L);

        value = 0L;
        TestTracker.Record("ldind.i8.Zero", *ptr == 0L);

        value = 1L;
        TestTracker.Record("ldind.i8.One", *ptr == 1L);

        value = -1L;
        TestTracker.Record("ldind.i8.Neg1", *ptr == -1L);

        value = long.MaxValue;
        TestTracker.Record("ldind.i8.Max", *ptr == long.MaxValue);

        value = long.MinValue;
        TestTracker.Record("ldind.i8.Min", *ptr == long.MinValue);

        // Value beyond int32 range
        value = 0x123456789ABCDEF0L;
        TestTracker.Record("ldind.i8.Large", *ptr == 0x123456789ABCDEF0L);

        // High 32 bits only set
        value = unchecked((long)0xFFFFFFFF00000000UL);
        TestTracker.Record("ldind.i8.High32", *ptr == unchecked((long)0xFFFFFFFF00000000UL));
    }

    #endregion

    #region ldind.i Tests (native int)

    private static unsafe void TestLdindI()
    {
        nint value = 12345;
        nint* ptr = &value;
        TestTracker.Record("ldind.i.Positive", *ptr == 12345);

        value = -12345;
        TestTracker.Record("ldind.i.Negative", *ptr == -12345);

        value = 0;
        TestTracker.Record("ldind.i.Zero", *ptr == 0);

        value = -1;
        TestTracker.Record("ldind.i.Neg1", *ptr == -1);

        // Large values (not using nint.MaxValue/MinValue which don't exist in korlib)
        value = 0x7FFFFFFF;  // Large positive value
        TestTracker.Record("ldind.i.Large", *ptr == 0x7FFFFFFF);

        // As pointer value
        int x = 42;
        value = (nint)(&x);
        TestTracker.Record("ldind.i.AsPointer", *ptr == (nint)(&x));
    }

    #endregion

    #region ldind.r4 Tests

    private static unsafe void TestLdindR4()
    {
        float value = 3.14f;
        float* ptr = &value;
        TestTracker.Record("ldind.r4.Basic", Assert.AreApproxEqual(3.14f, *ptr));

        value = 0.0f;
        TestTracker.Record("ldind.r4.Zero", *ptr == 0.0f);

        value = 1.0f;
        TestTracker.Record("ldind.r4.One", *ptr == 1.0f);

        value = -1.0f;
        TestTracker.Record("ldind.r4.Neg1", *ptr == -1.0f);

        value = float.MaxValue;
        TestTracker.Record("ldind.r4.Max", *ptr == float.MaxValue);

        value = float.MinValue;
        TestTracker.Record("ldind.r4.Min", *ptr == float.MinValue);

        value = float.Epsilon;
        TestTracker.Record("ldind.r4.Epsilon", *ptr == float.Epsilon);

        value = float.PositiveInfinity;
        TestTracker.Record("ldind.r4.PosInf", float.IsPositiveInfinity(*ptr));

        value = float.NegativeInfinity;
        TestTracker.Record("ldind.r4.NegInf", float.IsNegativeInfinity(*ptr));

        value = float.NaN;
        TestTracker.Record("ldind.r4.NaN", float.IsNaN(*ptr));
    }

    #endregion

    #region ldind.r8 Tests

    private static unsafe void TestLdindR8()
    {
        double value = 3.14159265358979;
        double* ptr = &value;
        TestTracker.Record("ldind.r8.Basic", Assert.AreApproxEqual(3.14159265358979, *ptr, 1e-12));

        value = 0.0;
        TestTracker.Record("ldind.r8.Zero", *ptr == 0.0);

        value = 1.0;
        TestTracker.Record("ldind.r8.One", *ptr == 1.0);

        value = -1.0;
        TestTracker.Record("ldind.r8.Neg1", *ptr == -1.0);

        value = double.MaxValue;
        TestTracker.Record("ldind.r8.Max", *ptr == double.MaxValue);

        value = double.MinValue;
        TestTracker.Record("ldind.r8.Min", *ptr == double.MinValue);

        value = double.Epsilon;
        TestTracker.Record("ldind.r8.Epsilon", *ptr == double.Epsilon);

        value = double.PositiveInfinity;
        TestTracker.Record("ldind.r8.PosInf", double.IsPositiveInfinity(*ptr));

        value = double.NegativeInfinity;
        TestTracker.Record("ldind.r8.NegInf", double.IsNegativeInfinity(*ptr));

        value = double.NaN;
        TestTracker.Record("ldind.r8.NaN", double.IsNaN(*ptr));
    }

    #endregion

    #region ldind.ref Tests

    private static void TestLdindRef()
    {
        // Basic reference load
        string str = "test";
        ref string r = ref str;
        TestTracker.Record("ldind.ref.Basic", r == "test");

        // Null reference
        object? obj = null;
        ref object? rObj = ref obj;
        TestTracker.Record("ldind.ref.Null", rObj == null);

        // Non-null object
        obj = "hello";
        TestTracker.Record("ldind.ref.NonNull", rObj == "hello");

        // Array reference
        int[] arr = new int[] { 1, 2, 3 };
        ref int[] rArr = ref arr;
        TestTracker.Record("ldind.ref.Array", rArr.Length == 3);

        // Derived type
        object baseRef = "derived";
        ref object rBase = ref baseRef;
        TestTracker.Record("ldind.ref.Derived", rBase is string);
    }

    #endregion

    #region stind.i1 Tests

    private static unsafe void TestStindI1()
    {
        sbyte value = 0;
        sbyte* ptr = &value;

        // Test using ldind.i1 (read via pointer) for proper sign-extension
        *ptr = 42;
        TestTracker.Record("stind.i1.Positive", *ptr == 42);

        *ptr = -42;
        TestTracker.Record("stind.i1.Negative", *ptr == -42);  // Read via ldind.i1 (sign-extends)

        *ptr = 0;
        TestTracker.Record("stind.i1.Zero", *ptr == 0);

        *ptr = sbyte.MaxValue;
        TestTracker.Record("stind.i1.Max", *ptr == 127);

        *ptr = sbyte.MinValue;
        TestTracker.Record("stind.i1.Min", *ptr == -128);  // Read via ldind.i1 (sign-extends)

        // Truncation test: store 0xFF via truncation, read via ldind.i1 should give -1
        *ptr = unchecked((sbyte)0x1FF);  // Truncates to 0xFF = -1
        TestTracker.Record("stind.i1.Truncate", *ptr == -1);  // Read via ldind.i1

        // Overwrite existing
        *ptr = 100;
        *ptr = 50;
        TestTracker.Record("stind.i1.Overwrite", *ptr == 50);
    }

    #endregion

    #region stind.i2 Tests

    private static unsafe void TestStindI2()
    {
        short value = 0;
        short* ptr = &value;

        // Test using ldind.i2 (read via pointer) for proper sign-extension
        *ptr = 1000;
        TestTracker.Record("stind.i2.Positive", *ptr == 1000);

        *ptr = -1000;
        TestTracker.Record("stind.i2.Negative", *ptr == -1000);  // Read via ldind.i2 (sign-extends)

        *ptr = 0;
        TestTracker.Record("stind.i2.Zero", *ptr == 0);

        *ptr = short.MaxValue;
        TestTracker.Record("stind.i2.Max", *ptr == 32767);

        *ptr = short.MinValue;
        TestTracker.Record("stind.i2.Min", *ptr == -32768);  // Read via ldind.i2 (sign-extends)

        // Truncation test: store 0xFFFF via truncation, read via ldind.i2 should give -1
        *ptr = unchecked((short)0x1FFFF);  // Truncates to 0xFFFF = -1
        TestTracker.Record("stind.i2.Truncate", *ptr == -1);  // Read via ldind.i2
    }

    #endregion

    #region stind.i4 Tests

    private static unsafe void TestStindI4()
    {
        int value = 0;
        int* ptr = &value;

        *ptr = 1000000;
        TestTracker.Record("stind.i4.Positive", value == 1000000);

        *ptr = -1000000;
        TestTracker.Record("stind.i4.Negative", value == -1000000);

        *ptr = 0;
        TestTracker.Record("stind.i4.Zero", value == 0);

        *ptr = int.MaxValue;
        TestTracker.Record("stind.i4.Max", value == int.MaxValue);

        *ptr = int.MinValue;
        TestTracker.Record("stind.i4.Min", value == int.MinValue);

        *ptr = unchecked((int)0xDEADBEEF);
        TestTracker.Record("stind.i4.Pattern", value == unchecked((int)0xDEADBEEF));
    }

    #endregion

    #region stind.i8 Tests

    private static unsafe void TestStindI8()
    {
        long value = 0;
        long* ptr = &value;

        *ptr = 1000000000000L;
        TestTracker.Record("stind.i8.Positive", value == 1000000000000L);

        *ptr = -1000000000000L;
        TestTracker.Record("stind.i8.Negative", value == -1000000000000L);

        *ptr = 0L;
        TestTracker.Record("stind.i8.Zero", value == 0L);

        *ptr = long.MaxValue;
        TestTracker.Record("stind.i8.Max", value == long.MaxValue);

        *ptr = long.MinValue;
        TestTracker.Record("stind.i8.Min", value == long.MinValue);

        *ptr = 0x123456789ABCDEF0L;
        TestTracker.Record("stind.i8.Large", value == 0x123456789ABCDEF0L);
    }

    #endregion

    #region stind.i Tests (native int)

    private static unsafe void TestStindI()
    {
        nint value = 0;
        nint* ptr = &value;

        *ptr = 12345;
        TestTracker.Record("stind.i.Positive", value == 12345);

        *ptr = -12345;
        TestTracker.Record("stind.i.Negative", value == -12345);

        *ptr = 0;
        TestTracker.Record("stind.i.Zero", value == 0);

        // Large values (not using nint.MaxValue/MinValue which don't exist in korlib)
        *ptr = 0x7FFFFFFF;  // Large positive value
        TestTracker.Record("stind.i.Large", value == 0x7FFFFFFF);
    }

    #endregion

    #region stind.r4 Tests

    private static unsafe void TestStindR4()
    {
        float value = 0;
        float* ptr = &value;

        *ptr = 3.14f;
        TestTracker.Record("stind.r4.Basic", Assert.AreApproxEqual(3.14f, value));

        *ptr = 0.0f;
        TestTracker.Record("stind.r4.Zero", value == 0.0f);

        *ptr = 1.0f;
        TestTracker.Record("stind.r4.One", value == 1.0f);

        *ptr = -1.0f;
        TestTracker.Record("stind.r4.Neg1", value == -1.0f);

        *ptr = float.MaxValue;
        TestTracker.Record("stind.r4.Max", value == float.MaxValue);

        *ptr = float.PositiveInfinity;
        TestTracker.Record("stind.r4.Inf", float.IsPositiveInfinity(value));

        *ptr = float.NaN;
        TestTracker.Record("stind.r4.NaN", float.IsNaN(value));
    }

    #endregion

    #region stind.r8 Tests

    private static unsafe void TestStindR8()
    {
        double value = 0;
        double* ptr = &value;

        *ptr = 3.14159;
        TestTracker.Record("stind.r8.Basic", Assert.AreApproxEqual(3.14159, value, 1e-5));

        *ptr = 0.0;
        TestTracker.Record("stind.r8.Zero", value == 0.0);

        *ptr = 1.0;
        TestTracker.Record("stind.r8.One", value == 1.0);

        *ptr = -1.0;
        TestTracker.Record("stind.r8.Neg1", value == -1.0);

        *ptr = double.MaxValue;
        TestTracker.Record("stind.r8.Max", value == double.MaxValue);

        *ptr = double.PositiveInfinity;
        TestTracker.Record("stind.r8.Inf", double.IsPositiveInfinity(value));

        *ptr = double.NaN;
        TestTracker.Record("stind.r8.NaN", double.IsNaN(value));
    }

    #endregion

    #region stind.ref Tests

    private static void TestStindRef()
    {
        string str = "old";
        ref string r = ref str;
        r = "new";
        TestTracker.Record("stind.ref.Basic", str == "new");

        // Store null
        object? obj = "something";
        ref object? rObj = ref obj;
        rObj = null;
        TestTracker.Record("stind.ref.Null", obj == null);

        // Store non-null
        rObj = "replaced";
        TestTracker.Record("stind.ref.NonNull", obj == "replaced");

        // Array reference
        int[] arr = new int[] { 1, 2, 3 };
        ref int[] rArr = ref arr;
        rArr = new int[] { 4, 5 };
        TestTracker.Record("stind.ref.Array", arr.Length == 2);

        // Overwrite existing
        string s = "first";
        ref string rs = ref s;
        rs = "second";
        rs = "third";
        TestTracker.Record("stind.ref.Overwrite", s == "third");
    }

    #endregion

    #region Round-Trip Tests

    private static unsafe void TestRoundTrip()
    {
        // Int8 round-trip
        sbyte sb = 0;
        sbyte* sbPtr = &sb;
        *sbPtr = -123;
        TestTracker.Record("roundtrip.i1", *sbPtr == -123);

        // UInt8 round-trip
        byte b = 0;
        byte* bPtr = &b;
        *bPtr = 234;
        TestTracker.Record("roundtrip.u1", *bPtr == 234);

        // Int16 round-trip
        short s = 0;
        short* sPtr = &s;
        *sPtr = -12345;
        TestTracker.Record("roundtrip.i2", *sPtr == -12345);

        // UInt16 round-trip
        ushort us = 0;
        ushort* usPtr = &us;
        *usPtr = 54321;
        TestTracker.Record("roundtrip.u2", *usPtr == 54321);

        // Int32 round-trip
        int i = 0;
        int* iPtr = &i;
        *iPtr = -123456789;
        TestTracker.Record("roundtrip.i4", *iPtr == -123456789);

        // UInt32 round-trip
        uint ui = 0;
        uint* uiPtr = &ui;
        *uiPtr = 3456789012;
        TestTracker.Record("roundtrip.u4", *uiPtr == 3456789012);

        // Int64 round-trip
        long l = 0;
        long* lPtr = &l;
        *lPtr = -1234567890123456789L;
        TestTracker.Record("roundtrip.i8", *lPtr == -1234567890123456789L);

        // Float round-trip
        float f = 0;
        float* fPtr = &f;
        *fPtr = 2.71828f;
        TestTracker.Record("roundtrip.r4", Assert.AreApproxEqual(2.71828f, *fPtr));

        // Double round-trip
        double d = 0;
        double* dPtr = &d;
        *dPtr = 1.41421356237;
        TestTracker.Record("roundtrip.r8", Assert.AreApproxEqual(1.41421356237, *dPtr, 1e-10));

        // Native int round-trip
        nint n = 0;
        nint* nPtr = &n;
        *nPtr = -98765;
        TestTracker.Record("roundtrip.i", *nPtr == -98765);
    }

    #endregion

    #region Array Element Access Tests

    private static unsafe void TestArrayElementAccess()
    {
        // Int array element access via pointer
        int[] intArr = new int[] { 10, 20, 30, 40, 50 };
        fixed (int* ptr = intArr)
        {
            TestTracker.Record("array.i4.Elem0", ptr[0] == 10);
            TestTracker.Record("array.i4.Elem2", ptr[2] == 30);
            TestTracker.Record("array.i4.Elem4", ptr[4] == 50);

            // Modify via pointer
            ptr[1] = 200;
            TestTracker.Record("array.i4.Modified", intArr[1] == 200);
        }

        // Byte array
        byte[] byteArr = new byte[] { 1, 2, 3, 4, 5 };
        fixed (byte* ptr = byteArr)
        {
            TestTracker.Record("array.u1.Elem0", ptr[0] == 1);
            ptr[2] = 30;
            TestTracker.Record("array.u1.Modified", byteArr[2] == 30);
        }

        // Long array
        long[] longArr = new long[] { 100L, 200L, 300L };
        fixed (long* ptr = longArr)
        {
            TestTracker.Record("array.i8.Elem1", ptr[1] == 200L);
            ptr[0] = 999L;
            TestTracker.Record("array.i8.Modified", longArr[0] == 999L);
        }

        // Float array
        float[] floatArr = new float[] { 1.5f, 2.5f, 3.5f };
        fixed (float* ptr = floatArr)
        {
            TestTracker.Record("array.r4.Elem2", Assert.AreApproxEqual(3.5f, ptr[2]));
        }

        // Double array
        double[] doubleArr = new double[] { 1.1, 2.2, 3.3 };
        fixed (double* ptr = doubleArr)
        {
            TestTracker.Record("array.r8.Elem1", Assert.AreApproxEqual(2.2, ptr[1]));
        }
    }

    #endregion

    #region ldloc Sign-Extension Tests (after stind)

    /// <summary>
    /// Tests that ldloc properly sign-extends sbyte locals after stind.i1 writes.
    /// This verifies the JIT generates MOVSX when loading sbyte locals.
    /// </summary>
    private static unsafe void TestLdlocSignExtendSByte()
    {
        sbyte value = 0;
        sbyte* ptr = &value;

        // Write via stind.i1, read via ldloc - should sign-extend
        *ptr = -42;
        TestTracker.Record("ldloc.sbyte.Negative", value == -42);  // ldloc must sign-extend

        *ptr = sbyte.MinValue;  // -128
        TestTracker.Record("ldloc.sbyte.Min", value == -128);  // ldloc must sign-extend

        *ptr = unchecked((sbyte)0xFF);  // -1
        TestTracker.Record("ldloc.sbyte.AllOnes", value == -1);  // ldloc must sign-extend

        *ptr = unchecked((sbyte)0x80);  // -128 (high bit set)
        TestTracker.Record("ldloc.sbyte.HighBit", value == -128);  // ldloc must sign-extend

        // Positive values should still work
        *ptr = 42;
        TestTracker.Record("ldloc.sbyte.Positive", value == 42);

        *ptr = sbyte.MaxValue;  // 127
        TestTracker.Record("ldloc.sbyte.Max", value == 127);
    }

    /// <summary>
    /// Tests that ldloc properly sign-extends short locals after stind.i2 writes.
    /// This verifies the JIT generates MOVSX when loading short locals.
    /// </summary>
    private static unsafe void TestLdlocSignExtendShort()
    {
        short value = 0;
        short* ptr = &value;

        // Write via stind.i2, read via ldloc - should sign-extend
        *ptr = -1000;
        TestTracker.Record("ldloc.short.Negative", value == -1000);  // ldloc must sign-extend

        *ptr = short.MinValue;  // -32768
        TestTracker.Record("ldloc.short.Min", value == -32768);  // ldloc must sign-extend

        *ptr = unchecked((short)0xFFFF);  // -1
        TestTracker.Record("ldloc.short.AllOnes", value == -1);  // ldloc must sign-extend

        *ptr = unchecked((short)0x8000);  // -32768 (high bit set)
        TestTracker.Record("ldloc.short.HighBit", value == -32768);  // ldloc must sign-extend

        // Positive values should still work
        *ptr = 1000;
        TestTracker.Record("ldloc.short.Positive", value == 1000);

        *ptr = short.MaxValue;  // 32767
        TestTracker.Record("ldloc.short.Max", value == 32767);
    }

    #endregion

    #region Additional Indirect Memory Tests

    public static void RunAdditionalTests()
    {
        TestPointerArithmetic();
        TestMultipleIndirections();
        TestMixedTypeAccess();
    }

    private static unsafe void TestPointerArithmetic()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        fixed (int* basePtr = arr)
        {
            // Pointer addition
            int* p1 = basePtr + 1;
            TestTracker.Record("ptrarith.Add1", *p1 == 20);

            int* p2 = basePtr + 2;
            TestTracker.Record("ptrarith.Add2", *p2 == 30);

            int* p4 = basePtr + 4;
            TestTracker.Record("ptrarith.Add4", *p4 == 50);

            // Pointer subtraction
            int* pEnd = basePtr + 4;
            int* pStart = pEnd - 4;
            TestTracker.Record("ptrarith.Sub4", *pStart == 10);

            // Pointer difference
            long diff = pEnd - basePtr;
            TestTracker.Record("ptrarith.Diff", diff == 4);

            // Pointer increment
            int* p = basePtr;
            p++;
            TestTracker.Record("ptrarith.Inc", *p == 20);
            p++;
            TestTracker.Record("ptrarith.Inc2", *p == 30);

            // Pointer decrement
            p--;
            TestTracker.Record("ptrarith.Dec", *p == 20);
        }

        // Byte pointer arithmetic
        byte[] bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        fixed (byte* bp = bytes)
        {
            byte* bp3 = bp + 3;
            TestTracker.Record("ptrarith.ByteAdd3", *bp3 == 4);

            byte* bp7 = bp + 7;
            TestTracker.Record("ptrarith.ByteAdd7", *bp7 == 8);
        }

        // Long pointer arithmetic
        long[] longs = new long[] { 100L, 200L, 300L };
        fixed (long* lp = longs)
        {
            long* lp1 = lp + 1;
            TestTracker.Record("ptrarith.LongAdd1", *lp1 == 200L);

            long* lp2 = lp + 2;
            TestTracker.Record("ptrarith.LongAdd2", *lp2 == 300L);
        }
    }

    private static unsafe void TestMultipleIndirections()
    {
        // Read and write same location multiple times
        int value = 0;
        int* ptr = &value;

        *ptr = 10;
        TestTracker.Record("multi.Write1", *ptr == 10);

        *ptr = *ptr + 5;
        TestTracker.Record("multi.RMW1", *ptr == 15);

        *ptr = *ptr * 2;
        TestTracker.Record("multi.RMW2", *ptr == 30);

        *ptr = *ptr - 10;
        TestTracker.Record("multi.RMW3", *ptr == 20);

        // Multiple pointers to same location
        int* ptr2 = &value;
        *ptr = 100;
        TestTracker.Record("multi.AliasRead", *ptr2 == 100);

        *ptr2 = 200;
        TestTracker.Record("multi.AliasWrite", *ptr == 200);

        // Array with multiple access patterns
        int[] arr = new int[] { 1, 2, 3, 4, 5 };
        fixed (int* p = arr)
        {
            int sum = 0;
            for (int i = 0; i < 5; i++)
                sum += p[i];
            TestTracker.Record("multi.ArraySum", sum == 15);

            // Reverse write
            for (int i = 0; i < 5; i++)
                p[i] = 5 - i;
            TestTracker.Record("multi.ArrayReverse", p[0] == 5 && p[4] == 1);

            // Verify through indexing
            TestTracker.Record("multi.ArrayVerify", arr[0] == 5 && arr[4] == 1);
        }
    }

    private static unsafe void TestMixedTypeAccess()
    {
        // Access int memory as bytes
        int intVal = 0x04030201;
        byte* bp = (byte*)&intVal;
        TestTracker.Record("mixed.IntAsByte0", bp[0] == 0x01);
        TestTracker.Record("mixed.IntAsByte1", bp[1] == 0x02);
        TestTracker.Record("mixed.IntAsByte2", bp[2] == 0x03);
        TestTracker.Record("mixed.IntAsByte3", bp[3] == 0x04);

        // Access long memory as ints
        long longVal = 0x0807060504030201L;
        int* ip = (int*)&longVal;
        TestTracker.Record("mixed.LongAsInt0", ip[0] == 0x04030201);
        TestTracker.Record("mixed.LongAsInt1", ip[1] == 0x08070605);

        // Access int memory as shorts
        int intVal2 = 0x44332211;
        short* sp = (short*)&intVal2;
        TestTracker.Record("mixed.IntAsShort0", (ushort)sp[0] == 0x2211);
        TestTracker.Record("mixed.IntAsShort1", (ushort)sp[1] == 0x4433);

        // Write through byte pointer, read as int
        int result = 0;
        byte* rbp = (byte*)&result;
        rbp[0] = 0xAB;
        rbp[1] = 0xCD;
        rbp[2] = 0xEF;
        rbp[3] = 0x12;
        TestTracker.Record("mixed.ByteWriteIntRead", (uint)result == 0x12EFCDAB);

        // Write through short pointer, read as int
        int result2 = 0;
        short* rsp = (short*)&result2;
        rsp[0] = unchecked((short)0xBEEF);
        rsp[1] = unchecked((short)0xDEAD);
        TestTracker.Record("mixed.ShortWriteIntRead", (uint)result2 == 0xDEADBEEF);
    }

    #endregion
}
