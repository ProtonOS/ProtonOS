// JITTest - Array Operation Tests
// Section 19: newarr, ldlen, ldelem.*, stelem.*, ldelema

namespace JITTest;

/// <summary>
/// Tests for array operation instructions
/// </summary>
public static class ArrayOperationTests
{
    public static void RunAll()
    {
        TestNewarr();
        TestLdlen();
        TestLdelem();
        TestStelem();
        TestLdelema();
    }

    #region newarr (0x8D)

    private static void TestNewarr()
    {
        var intArr = new int[10];
        TestTracker.Record("newarr.Int32", intArr != null && intArr.Length == 10);

        var longArr = new long[5];
        TestTracker.Record("newarr.Int64", longArr.Length == 5);

        var strArr = new string[3];
        TestTracker.Record("newarr.String", strArr.Length == 3);

        var emptyArr = new int[0];
        TestTracker.Record("newarr.Empty", emptyArr.Length == 0);
    }

    #endregion

    #region ldlen (0x8E)

    private static void TestLdlen()
    {
        TestTracker.Record("ldlen.Zero", new int[0].Length == 0);
        TestTracker.Record("ldlen.One", new int[1].Length == 1);
        TestTracker.Record("ldlen.Large", new int[1000].Length == 1000);
    }

    #endregion

    #region ldelem.* (0x90-0x9A)

    private static void TestLdelem()
    {
        // ldelem.i1 / ldelem.u1
        var byteArr = new byte[] { 1, 2, 3 };
        TestTracker.Record("ldelem.u1", byteArr[1] == 2);

        var sbyteArr = new sbyte[] { -1, -2, -3 };
        TestTracker.Record("ldelem.i1", sbyteArr[1] == -2);

        // ldelem.i2 / ldelem.u2
        var shortArr = new short[] { -1000, 2000 };
        TestTracker.Record("ldelem.i2", shortArr[0] == -1000);

        // ldelem.i4 / ldelem.u4
        var intArr = new int[] { 1, 2, 42, 4 };
        TestTracker.Record("ldelem.i4", intArr[2] == 42);

        // ldelem.i8
        var longArr = new long[] { 1L, 0x123456789ABCDEF0L };
        TestTracker.Record("ldelem.i8", longArr[1] == 0x123456789ABCDEF0L);

        // ldelem.r4
        var floatArr = new float[] { 1.0f, 3.14f };
        TestTracker.Record("ldelem.r4", Assert.AreApproxEqual(3.14f, floatArr[1]));

        // ldelem.r8
        var doubleArr = new double[] { 1.0, 3.14159 };
        TestTracker.Record("ldelem.r8", Assert.AreApproxEqual(3.14159, doubleArr[1], 1e-5));

        // ldelem.ref
        var strArr = new string[] { "a", "b", "c" };
        TestTracker.Record("ldelem.ref", strArr[1] == "b");
    }

    #endregion

    #region stelem.* (0x9B-0xA2)

    private static void TestStelem()
    {
        // stelem.i1
        var sbyteArr = new sbyte[3];
        sbyteArr[1] = -42;
        TestTracker.Record("stelem.i1", sbyteArr[1] == -42);

        // stelem.i2
        var shortArr = new short[3];
        shortArr[1] = -1000;
        TestTracker.Record("stelem.i2", shortArr[1] == -1000);

        // stelem.i4
        var intArr = new int[3];
        intArr[1] = 42;
        TestTracker.Record("stelem.i4", intArr[1] == 42);

        // stelem.i8
        var longArr = new long[3];
        longArr[1] = 0x123456789ABCDEF0L;
        TestTracker.Record("stelem.i8", longArr[1] == 0x123456789ABCDEF0L);

        // stelem.r4
        var floatArr = new float[3];
        floatArr[1] = 3.14f;
        TestTracker.Record("stelem.r4", Assert.AreApproxEqual(3.14f, floatArr[1]));

        // stelem.r8
        var doubleArr = new double[3];
        doubleArr[1] = 3.14159;
        TestTracker.Record("stelem.r8", Assert.AreApproxEqual(3.14159, doubleArr[1], 1e-5));

        // stelem.ref
        var strArr = new string?[3];
        strArr[1] = "test";
        TestTracker.Record("stelem.ref", strArr[1] == "test");
    }

    #endregion

    #region ldelema (0x8F)

    private static unsafe void TestLdelema()
    {
        var arr = new int[] { 1, 2, 42, 4 };
        fixed (int* ptr = &arr[2])
        {
            TestTracker.Record("ldelema.Read", *ptr == 42);
            *ptr = 100;
        }
        TestTracker.Record("ldelema.Write", arr[2] == 100);
    }

    #endregion
}
