// JITTest - Base Instruction Tests
// Section 1: nop (0x00), break (0x01), ldarg.0-3 (0x02-0x05), ldloc.0-3 (0x06-0x09), stloc.0-3 (0x0A-0x0D)

namespace JITTest;

/// <summary>
/// Tests for base IL instructions (opcodes 0x00-0x0D)
/// </summary>
public static class BaseInstructionTests
{
    public static void RunAll()
    {
        // nop tests
        TestNop();

        // ldarg tests
        TestLdarg0();
        TestLdarg1();
        TestLdarg2();
        TestLdarg3();

        // ldloc tests
        TestLdloc0();
        TestLdloc1();
        TestLdloc2();
        TestLdloc3();

        // stloc tests
        TestStloc0();
        TestStloc1();
        TestStloc2();
        TestStloc3();
    }

    #region nop (0x00)

    private static void TestNop()
    {
        // Test 1: Basic nop execution - verify nop doesn't affect program state
        int x = 42;
        // nop would be here in IL
        TestTracker.Record("nop.BasicExecution", x == 42);

        // Test 2: Multiple consecutive nops
        int y = 1;
        y++;
        // nops here
        y++;
        TestTracker.Record("nop.MultipleConsecutive", y == 3);

        // Test 3: Nop between operations
        int a = 10;
        // nop
        int b = 20;
        // nop
        int c = a + b;
        TestTracker.Record("nop.BetweenOperations", c == 30);
    }

    #endregion

    #region ldarg.0-3 (0x02-0x05)

    private static void TestLdarg0()
    {
        // Static method - ldarg.0 loads first parameter
        TestTracker.Record("ldarg.0.Int32", Ldarg0Helper_Int32(42) == 42);
        TestTracker.Record("ldarg.0.Int64", Ldarg0Helper_Int64(0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.0.Float", Assert.AreApproxEqual(3.14f, Ldarg0Helper_Float(3.14f)));
        TestTracker.Record("ldarg.0.Double", Assert.AreApproxEqual(3.14159265358979, Ldarg0Helper_Double(3.14159265358979)));
        TestTracker.Record("ldarg.0.Object", Ldarg0Helper_Object("test") == "test");
        TestTracker.Record("ldarg.0.Null", Ldarg0Helper_Object(null) == null);
    }

    private static int Ldarg0Helper_Int32(int arg0) => arg0;
    private static long Ldarg0Helper_Int64(long arg0) => arg0;
    private static float Ldarg0Helper_Float(float arg0) => arg0;
    private static double Ldarg0Helper_Double(double arg0) => arg0;
    private static object? Ldarg0Helper_Object(object? arg0) => arg0;

    private static void TestLdarg1()
    {
        TestTracker.Record("ldarg.1.Int32", Ldarg1Helper_Int32(0, 42) == 42);
        TestTracker.Record("ldarg.1.Int64", Ldarg1Helper_Int64(0, 0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.1.MixedTypes", Ldarg1Helper_Mixed(1, 2L) == 2L);
    }

    private static int Ldarg1Helper_Int32(int arg0, int arg1) => arg1;
    private static long Ldarg1Helper_Int64(int arg0, long arg1) => arg1;
    private static long Ldarg1Helper_Mixed(int arg0, long arg1) => arg1;

    private static void TestLdarg2()
    {
        TestTracker.Record("ldarg.2.Int32", Ldarg2Helper_Int32(0, 0, 42) == 42);
        TestTracker.Record("ldarg.2.WithPrevious", Ldarg2Helper_Sum(1, 2, 3) == 6);
    }

    private static int Ldarg2Helper_Int32(int a, int b, int c) => c;
    private static int Ldarg2Helper_Sum(int a, int b, int c) => a + b + c;

    private static void TestLdarg3()
    {
        TestTracker.Record("ldarg.3.Int32", Ldarg3Helper_Int32(0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.3.AllTypes", Ldarg3Helper_AllTypes(1, 2L, 3.0f, 4.0) == 4.0);
    }

    private static int Ldarg3Helper_Int32(int a, int b, int c, int d) => d;
    private static double Ldarg3Helper_AllTypes(int a, long b, float c, double d) => d;

    #endregion

    #region ldloc.0-3 (0x06-0x09)

    private static void TestLdloc0()
    {
        int local0 = 42;
        TestTracker.Record("ldloc.0.Int32", local0 == 42);

        long local0L = 0x123456789ABCDEF0L;
        TestTracker.Record("ldloc.0.Int64", local0L == 0x123456789ABCDEF0L);

        float local0F = 3.14f;
        TestTracker.Record("ldloc.0.Float", Assert.AreApproxEqual(3.14f, local0F));

        object? local0O = "test";
        TestTracker.Record("ldloc.0.Object", local0O == "test");
    }

    private static void TestLdloc1()
    {
        int a = 1;
        int b = 42;
        TestTracker.Record("ldloc.1.Int32", b == 42);
        TestTracker.Record("ldloc.1.AfterLdloc0", a + b == 43);
    }

    private static void TestLdloc2()
    {
        int a = 1, b = 2, c = 42;
        TestTracker.Record("ldloc.2.Int32", c == 42);
        TestTracker.Record("ldloc.2.Sum", a + b + c == 45);
    }

    private static void TestLdloc3()
    {
        int a = 1, b = 2, c = 3, d = 42;
        TestTracker.Record("ldloc.3.Int32", d == 42);
        TestTracker.Record("ldloc.3.Sum", a + b + c + d == 48);
    }

    #endregion

    #region stloc.0-3 (0x0A-0x0D)

    private static void TestStloc0()
    {
        int x;
        x = 42;
        TestTracker.Record("stloc.0.Int32", x == 42);

        x = 100;
        TestTracker.Record("stloc.0.Reassign", x == 100);
    }

    private static void TestStloc1()
    {
        int a = 0;
        int b;
        b = 42;
        TestTracker.Record("stloc.1.Int32", b == 42);
        _ = a; // Suppress unused warning
    }

    private static void TestStloc2()
    {
        int a = 0, b = 0;
        int c;
        c = 42;
        TestTracker.Record("stloc.2.Int32", c == 42);
        _ = a; _ = b;
    }

    private static void TestStloc3()
    {
        int a = 0, b = 0, c = 0;
        int d;
        d = 42;
        TestTracker.Record("stloc.3.Int32", d == 42);
        _ = a; _ = b; _ = c;
    }

    #endregion
}
