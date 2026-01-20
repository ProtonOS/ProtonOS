// JITTest - Argument/Local Short Form Tests
// Section 2: ldarg.s (0x0E), ldarga.s (0x0F), starg.s (0x10), ldloc.s (0x11), ldloca.s (0x12), stloc.s (0x13)

namespace JITTest;

/// <summary>
/// Tests for short-form argument and local variable instructions
/// </summary>
public static class ArgumentLocalShortTests
{
    public static void RunAll()
    {
        // ldarg.s tests
        TestLdargS();

        // ldarga.s tests
        TestLdargaS();

        // starg.s tests
        TestStargS();

        // ldloc.s tests
        TestLdlocS();

        // ldloca.s tests
        TestLdlocaS();

        // stloc.s tests
        TestStlocS();
    }

    #region ldarg.s (0x0E)

    private static void TestLdargS()
    {
        // ldarg.s is used for argument indices 4-255
        TestTracker.Record("ldarg.s.Arg4", LdargSHelper(0, 0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.s.Arg5", LdargSHelper5(0, 0, 0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.s.MixedTypes", LdargSMixedHelper(0, 0, 0, 0, 3.14f) == 3.14f);
    }

    private static int LdargSHelper(int a, int b, int c, int d, int e) => e;
    private static int LdargSHelper5(int a, int b, int c, int d, int e, int f) => f;
    private static float LdargSMixedHelper(int a, int b, int c, int d, float e) => e;

    #endregion

    #region ldarga.s (0x0F)

    private static void TestLdargaS()
    {
        TestTracker.Record("ldarga.s.ReadViaPointer", LdargaSHelper(42) == 42);
        TestTracker.Record("ldarga.s.StructByRef", LdargaSStructHelper(new TestStruct { Value = 100 }) == 100);
    }

    private static unsafe int LdargaSHelper(int arg)
    {
        // ldarga.s loads address of argument
        int* ptr = &arg;
        return *ptr;
    }

    private static unsafe int LdargaSStructHelper(TestStruct s)
    {
        TestStruct* ptr = &s;
        return ptr->Value;
    }

    private struct TestStruct { public int Value; }

    #endregion

    #region starg.s (0x10)

    private static void TestStargS()
    {
        TestTracker.Record("starg.s.Reassign", StargSHelper(10) == 42);
        TestTracker.Record("starg.s.MultipleReassign", StargSMultipleHelper(0) == 30);
    }

    private static int StargSHelper(int arg)
    {
        arg = 42; // starg.s
        return arg;
    }

    private static int StargSMultipleHelper(int arg)
    {
        arg = 10;
        arg = 20;
        arg = 30;
        return arg;
    }

    #endregion

    #region ldloc.s (0x11)

    private static void TestLdlocS()
    {
        // ldloc.s is used for local indices 4-255
        int l0 = 0, l1 = 0, l2 = 0, l3 = 0;
        int l4 = 42;
        TestTracker.Record("ldloc.s.Local4", l4 == 42);
        _ = l0; _ = l1; _ = l2; _ = l3;

        // More locals to force ldloc.s
        int l5 = 50, l6 = 60;
        TestTracker.Record("ldloc.s.Local5", l5 == 50);
        TestTracker.Record("ldloc.s.Local6", l6 == 60);
    }

    #endregion

    #region ldloca.s (0x12)

    private static void TestLdlocaS()
    {
        int l0 = 0, l1 = 0, l2 = 0, l3 = 0;
        int l4 = 42;
        _ = l0; _ = l1; _ = l2; _ = l3;

        unsafe
        {
            int* ptr = &l4;
            TestTracker.Record("ldloca.s.ReadViaPointer", *ptr == 42);
        }
    }

    #endregion

    #region stloc.s (0x13)

    private static void TestStlocS()
    {
        int l0 = 0, l1 = 0, l2 = 0, l3 = 0;
        int l4;
        l4 = 42; // stloc.s
        TestTracker.Record("stloc.s.Store", l4 == 42);
        _ = l0; _ = l1; _ = l2; _ = l3;

        l4 = 100; // stloc.s reassign
        TestTracker.Record("stloc.s.Reassign", l4 == 100);
    }

    #endregion
}
