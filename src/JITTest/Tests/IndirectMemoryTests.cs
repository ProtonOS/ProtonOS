// JITTest - Indirect Memory Tests
// Sections 9-10: ldind.* (0x46-0x50), stind.* (0x51-0x57)

namespace JITTest;

/// <summary>
/// Tests for indirect load and store instructions
/// </summary>
public static class IndirectMemoryTests
{
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
    }

    #region ldind.* Tests

    private static unsafe void TestLdindI1()
    {
        sbyte value = -42;
        sbyte* ptr = &value;
        TestTracker.Record("ldind.i1.Positive", LoadI1((sbyte*)&value, 42) == 42);
        value = -42;
        TestTracker.Record("ldind.i1.Negative", *ptr == -42);
        value = sbyte.MaxValue;
        TestTracker.Record("ldind.i1.MaxValue", *ptr == sbyte.MaxValue);
        value = sbyte.MinValue;
        TestTracker.Record("ldind.i1.MinValue", *ptr == sbyte.MinValue);
    }

    private static unsafe sbyte LoadI1(sbyte* ptr, sbyte val) { *ptr = val; return *ptr; }

    private static unsafe void TestLdindU1()
    {
        byte value = 200;
        byte* ptr = &value;
        TestTracker.Record("ldind.u1.Basic", *ptr == 200);
        value = byte.MaxValue;
        TestTracker.Record("ldind.u1.MaxValue", *ptr == byte.MaxValue);
    }

    private static unsafe void TestLdindI2()
    {
        short value = -1000;
        short* ptr = &value;
        TestTracker.Record("ldind.i2.Negative", *ptr == -1000);
        value = short.MaxValue;
        TestTracker.Record("ldind.i2.MaxValue", *ptr == short.MaxValue);
    }

    private static unsafe void TestLdindU2()
    {
        ushort value = 50000;
        ushort* ptr = &value;
        TestTracker.Record("ldind.u2.Basic", *ptr == 50000);
    }

    private static unsafe void TestLdindI4()
    {
        int value = -1000000;
        int* ptr = &value;
        TestTracker.Record("ldind.i4.Negative", *ptr == -1000000);
        value = int.MaxValue;
        TestTracker.Record("ldind.i4.MaxValue", *ptr == int.MaxValue);
        value = int.MinValue;
        TestTracker.Record("ldind.i4.MinValue", *ptr == int.MinValue);
    }

    private static unsafe void TestLdindU4()
    {
        uint value = 3000000000;
        uint* ptr = &value;
        TestTracker.Record("ldind.u4.Basic", *ptr == 3000000000);
    }

    private static unsafe void TestLdindI8()
    {
        long value = -1000000000000L;
        long* ptr = &value;
        TestTracker.Record("ldind.i8.Negative", *ptr == -1000000000000L);
        value = long.MaxValue;
        TestTracker.Record("ldind.i8.MaxValue", *ptr == long.MaxValue);
    }

    private static unsafe void TestLdindI()
    {
        nint value = -12345;
        nint* ptr = &value;
        TestTracker.Record("ldind.i.Basic", *ptr == -12345);
    }

    private static unsafe void TestLdindR4()
    {
        float value = 3.14f;
        float* ptr = &value;
        TestTracker.Record("ldind.r4.Basic", Assert.AreApproxEqual(3.14f, *ptr));
    }

    private static unsafe void TestLdindR8()
    {
        double value = 3.14159265358979;
        double* ptr = &value;
        TestTracker.Record("ldind.r8.Basic", Assert.AreApproxEqual(3.14159265358979, *ptr, 1e-12));
    }

    private static void TestLdindRef()
    {
        // ldind.ref is tested through managed references
        string str = "test";
        ref string r = ref str;
        TestTracker.Record("ldind.ref.Basic", r == "test");
    }

    #endregion

    #region stind.* Tests

    private static unsafe void TestStindI1()
    {
        sbyte value = 0;
        sbyte* ptr = &value;
        *ptr = 42;
        TestTracker.Record("stind.i1.Basic", value == 42);
    }

    private static unsafe void TestStindI2()
    {
        short value = 0;
        short* ptr = &value;
        *ptr = 1000;
        TestTracker.Record("stind.i2.Basic", value == 1000);
    }

    private static unsafe void TestStindI4()
    {
        int value = 0;
        int* ptr = &value;
        *ptr = 1000000;
        TestTracker.Record("stind.i4.Basic", value == 1000000);
    }

    private static unsafe void TestStindI8()
    {
        long value = 0;
        long* ptr = &value;
        *ptr = 1000000000000L;
        TestTracker.Record("stind.i8.Basic", value == 1000000000000L);
    }

    private static unsafe void TestStindI()
    {
        nint value = 0;
        nint* ptr = &value;
        *ptr = 12345;
        TestTracker.Record("stind.i.Basic", value == 12345);
    }

    private static unsafe void TestStindR4()
    {
        float value = 0;
        float* ptr = &value;
        *ptr = 3.14f;
        TestTracker.Record("stind.r4.Basic", Assert.AreApproxEqual(3.14f, value));
    }

    private static unsafe void TestStindR8()
    {
        double value = 0;
        double* ptr = &value;
        *ptr = 3.14159;
        TestTracker.Record("stind.r8.Basic", Assert.AreApproxEqual(3.14159, value, 1e-5));
    }

    private static void TestStindRef()
    {
        string str = "old";
        ref string r = ref str;
        r = "new";
        TestTracker.Record("stind.ref.Basic", str == "new");
    }

    #endregion
}
