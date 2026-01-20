// JITTest - Field Operation Tests
// Section 17: ldfld, stfld, ldsfld, stsfld, ldflda, ldsflda

namespace JITTest;

/// <summary>
/// Tests for field operation instructions
/// </summary>
public static class FieldOperationTests
{
    private static int s_staticInt;
    private static long s_staticLong;
    private static string? s_staticString;

    public static void RunAll()
    {
        TestLdfld();
        TestStfld();
        TestLdsfld();
        TestStsfld();
        TestLdflda();
        TestLdsflda();
    }

    #region ldfld (0x7B)

    private static void TestLdfld()
    {
        var obj = new FieldTestClass { IntField = 42, LongField = 100L, StringField = "test" };
        TestTracker.Record("ldfld.Int32", obj.IntField == 42);
        TestTracker.Record("ldfld.Int64", obj.LongField == 100L);
        TestTracker.Record("ldfld.String", obj.StringField == "test");

        var s = new FieldTestStruct { Value = 10 };
        TestTracker.Record("ldfld.Struct", s.Value == 10);
    }

    #endregion

    #region stfld (0x7D)

    private static void TestStfld()
    {
        var obj = new FieldTestClass();
        obj.IntField = 42;
        TestTracker.Record("stfld.Int32", obj.IntField == 42);

        obj.LongField = 100L;
        TestTracker.Record("stfld.Int64", obj.LongField == 100L);

        obj.StringField = "test";
        TestTracker.Record("stfld.String", obj.StringField == "test");
    }

    #endregion

    #region ldsfld (0x7E)

    private static void TestLdsfld()
    {
        s_staticInt = 42;
        TestTracker.Record("ldsfld.Int32", s_staticInt == 42);

        s_staticLong = 100L;
        TestTracker.Record("ldsfld.Int64", s_staticLong == 100L);

        s_staticString = "test";
        TestTracker.Record("ldsfld.String", s_staticString == "test");
    }

    #endregion

    #region stsfld (0x80)

    private static void TestStsfld()
    {
        s_staticInt = 0;
        s_staticInt = 42;
        TestTracker.Record("stsfld.Int32", s_staticInt == 42);

        s_staticString = null;
        s_staticString = "test";
        TestTracker.Record("stsfld.String", s_staticString == "test");
    }

    #endregion

    #region ldflda (0x7C)

    private static unsafe void TestLdflda()
    {
        var obj = new FieldTestClass { IntField = 42 };
        fixed (int* ptr = &obj.IntField)
        {
            TestTracker.Record("ldflda.Read", *ptr == 42);
            *ptr = 100;
        }
        TestTracker.Record("ldflda.Write", obj.IntField == 100);
    }

    #endregion

    #region ldsflda (0x7F)

    private static unsafe void TestLdsflda()
    {
        s_staticInt = 42;
        fixed (int* ptr = &s_staticInt)
        {
            TestTracker.Record("ldsflda.Read", *ptr == 42);
            *ptr = 100;
        }
        TestTracker.Record("ldsflda.Write", s_staticInt == 100);
    }

    #endregion

    private class FieldTestClass
    {
        public int IntField;
        public long LongField;
        public string? StringField;
    }

    private struct FieldTestStruct
    {
        public int Value;
    }
}
