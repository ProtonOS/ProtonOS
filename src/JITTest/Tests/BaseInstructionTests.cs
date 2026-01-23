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
        // nop tests (3 tests)
        TestNop();

        // break tests - skipped, requires debugger infrastructure
        // TestBreak();

        // ldarg tests (66 tests for ldarg.0-3)
        TestLdarg0();
        TestLdarg1();
        TestLdarg2();
        TestLdarg3();

        // ldloc tests (62 tests for ldloc.0-3)
        TestLdloc0();
        TestLdloc1();
        TestLdloc2();
        TestLdloc3();

        // stloc tests (54 tests for stloc.0-3)
        TestStloc0();
        TestStloc1();
        TestStloc2();
        TestStloc3();
    }

    // Helper struct for value type tests
    private struct SmallStruct
    {
        public int X;
        public int Y;
    }

    private struct LargeStruct
    {
        public long A;
        public long B;
        public long C;
    }

    private struct StructWithRef
    {
        public int Value;
        public object? Ref;
    }

    // Helper class for instance method tests
    private class TestClass
    {
        public int Value;

        public int GetThis() => Value;
        public int GetArg1(int arg1) => arg1;
        public int GetArg2(int a, int arg2) => arg2;
        public int GetArg3(int a, int b, int arg3) => arg3;
    }

    private struct TestStruct
    {
        public int Value;

        public int GetThisValue() => Value;
        public int GetArg1(int arg1) => arg1;
    }

    #region nop (0x00) - 3 tests

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

    #region ldarg.0 (0x02) - 18 tests

    private static void TestLdarg0()
    {
        // Static method tests - ldarg.0 loads first parameter

        // Primitive types
        TestTracker.Record("ldarg.0.Int8", Ldarg0_SByte((sbyte)-42) == -42);
        TestTracker.Record("ldarg.0.UInt8", Ldarg0_Byte((byte)200) == 200);
        TestTracker.Record("ldarg.0.Int16", Ldarg0_Int16((short)-1000) == -1000);
        TestTracker.Record("ldarg.0.UInt16", Ldarg0_UInt16((ushort)50000) == 50000);
        TestTracker.Record("ldarg.0.Int32", Ldarg0_Int32(42) == 42);
        TestTracker.Record("ldarg.0.UInt32", Ldarg0_UInt32(3000000000u) == 3000000000u);
        TestTracker.Record("ldarg.0.Int64", Ldarg0_Int64(0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.0.UInt64", Ldarg0_UInt64(0xFEDCBA9876543210UL) == 0xFEDCBA9876543210UL);
        TestTracker.Record("ldarg.0.Float32", Assert.AreApproxEqual(3.14f, Ldarg0_Float(3.14f)));
        TestTracker.Record("ldarg.0.Float64", Assert.AreApproxEqual(3.14159265358979, Ldarg0_Double(3.14159265358979)));

        // Reference types
        TestTracker.Record("ldarg.0.Object", Ldarg0_Object("test") == "test");
        TestTracker.Record("ldarg.0.Null", Ldarg0_Object(null) == null);

        // Value types (structs)
        var small = new SmallStruct { X = 10, Y = 20 };
        var result = Ldarg0_SmallStruct(small);
        TestTracker.Record("ldarg.0.SmallStruct", result.X == 10 && result.Y == 20);

        // Managed pointer (byref)
        int refVal = 42;
        TestTracker.Record("ldarg.0.ByRef", Ldarg0_ByRef(ref refVal) == 42);

        // Instance method tests - ldarg.0 loads 'this' pointer
        var obj = new TestClass { Value = 100 };
        TestTracker.Record("ldarg.0.ThisClass", obj.GetThis() == 100);

        var structVal = new TestStruct { Value = 200 };
        TestTracker.Record("ldarg.0.ThisStruct", structVal.GetThisValue() == 200);

        // Additional edge cases
        TestTracker.Record("ldarg.0.NegativeInt32", Ldarg0_Int32(-2147483648) == int.MinValue);
        TestTracker.Record("ldarg.0.MaxInt32", Ldarg0_Int32(2147483647) == int.MaxValue);
    }

    private static int Ldarg0_SByte(sbyte arg0) => arg0;
    private static int Ldarg0_Byte(byte arg0) => arg0;
    private static int Ldarg0_Int16(short arg0) => arg0;
    private static int Ldarg0_UInt16(ushort arg0) => arg0;
    private static int Ldarg0_Int32(int arg0) => arg0;
    private static uint Ldarg0_UInt32(uint arg0) => arg0;
    private static long Ldarg0_Int64(long arg0) => arg0;
    private static ulong Ldarg0_UInt64(ulong arg0) => arg0;
    private static float Ldarg0_Float(float arg0) => arg0;
    private static double Ldarg0_Double(double arg0) => arg0;
    private static object? Ldarg0_Object(object? arg0) => arg0;
    private static SmallStruct Ldarg0_SmallStruct(SmallStruct arg0) => arg0;
    private static int Ldarg0_ByRef(ref int arg0) => arg0;

    #endregion

    #region ldarg.1 (0x03) - 16 tests

    private static void TestLdarg1()
    {
        // Primitive types
        TestTracker.Record("ldarg.1.Int8", Ldarg1_SByte(0, (sbyte)-42) == -42);
        TestTracker.Record("ldarg.1.UInt8", Ldarg1_Byte(0, (byte)200) == 200);
        TestTracker.Record("ldarg.1.Int16", Ldarg1_Int16(0, (short)-1000) == -1000);
        TestTracker.Record("ldarg.1.UInt16", Ldarg1_UInt16(0, (ushort)50000) == 50000);
        TestTracker.Record("ldarg.1.Int32", Ldarg1_Int32(0, 42) == 42);
        TestTracker.Record("ldarg.1.UInt32", Ldarg1_UInt32(0, 3000000000u) == 3000000000u);
        TestTracker.Record("ldarg.1.Int64", Ldarg1_Int64(0, 0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.1.UInt64", Ldarg1_UInt64(0, 0xFEDCBA9876543210UL) == 0xFEDCBA9876543210UL);
        TestTracker.Record("ldarg.1.Float32", Assert.AreApproxEqual(3.14f, Ldarg1_Float(0, 3.14f)));
        TestTracker.Record("ldarg.1.Float64", Assert.AreApproxEqual(2.71828, Ldarg1_Double(0, 2.71828)));

        // Reference types
        TestTracker.Record("ldarg.1.Object", Ldarg1_Object(null, "test") == "test");
        TestTracker.Record("ldarg.1.Null", Ldarg1_Object("first", null) == null);

        // Value types
        var small = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldarg.1.Struct", Ldarg1_Struct(0, small).X == 10);

        // Byref
        int refVal = 42;
        TestTracker.Record("ldarg.1.ByRef", Ldarg1_ByRef(0, ref refVal) == 42);

        // Instance method - arg1 is first param after 'this'
        var obj = new TestClass { Value = 0 };
        TestTracker.Record("ldarg.1.Instance", obj.GetArg1(42) == 42);

        // Mixed types - arg0 different from arg1
        TestTracker.Record("ldarg.1.MixedTypes", Ldarg1_Mixed(1, 2L) == 2L);
    }

    private static int Ldarg1_SByte(int a, sbyte arg1) => arg1;
    private static int Ldarg1_Byte(int a, byte arg1) => arg1;
    private static int Ldarg1_Int16(int a, short arg1) => arg1;
    private static int Ldarg1_UInt16(int a, ushort arg1) => arg1;
    private static int Ldarg1_Int32(int a, int arg1) => arg1;
    private static uint Ldarg1_UInt32(int a, uint arg1) => arg1;
    private static long Ldarg1_Int64(int a, long arg1) => arg1;
    private static ulong Ldarg1_UInt64(int a, ulong arg1) => arg1;
    private static float Ldarg1_Float(int a, float arg1) => arg1;
    private static double Ldarg1_Double(int a, double arg1) => arg1;
    private static object? Ldarg1_Object(object? a, object? arg1) => arg1;
    private static SmallStruct Ldarg1_Struct(int a, SmallStruct arg1) => arg1;
    private static int Ldarg1_ByRef(int a, ref int arg1) => arg1;
    private static long Ldarg1_Mixed(int a, long arg1) => arg1;

    #endregion

    #region ldarg.2 (0x04) - 16 tests

    private static void TestLdarg2()
    {
        // Primitive types
        TestTracker.Record("ldarg.2.Int8", Ldarg2_SByte(0, 0, (sbyte)-42) == -42);
        TestTracker.Record("ldarg.2.UInt8", Ldarg2_Byte(0, 0, (byte)200) == 200);
        TestTracker.Record("ldarg.2.Int16", Ldarg2_Int16(0, 0, (short)-1000) == -1000);
        TestTracker.Record("ldarg.2.UInt16", Ldarg2_UInt16(0, 0, (ushort)50000) == 50000);
        TestTracker.Record("ldarg.2.Int32", Ldarg2_Int32(0, 0, 42) == 42);
        TestTracker.Record("ldarg.2.UInt32", Ldarg2_UInt32(0, 0, 3000000000u) == 3000000000u);
        TestTracker.Record("ldarg.2.Int64", Ldarg2_Int64(0, 0, 0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.2.UInt64", Ldarg2_UInt64(0, 0, 0xFEDCBA9876543210UL) == 0xFEDCBA9876543210UL);
        TestTracker.Record("ldarg.2.Float32", Assert.AreApproxEqual(3.14f, Ldarg2_Float(0, 0, 3.14f)));
        TestTracker.Record("ldarg.2.Float64", Assert.AreApproxEqual(2.71828, Ldarg2_Double(0, 0, 2.71828)));

        // Reference types
        TestTracker.Record("ldarg.2.Object", Ldarg2_Object(null, null, "test") == "test");
        TestTracker.Record("ldarg.2.Null", Ldarg2_Object("a", "b", null) == null);

        // Value types
        var small = new SmallStruct { X = 30, Y = 40 };
        TestTracker.Record("ldarg.2.Struct", Ldarg2_Struct(0, 0, small).Y == 40);

        // Byref
        int refVal = 99;
        TestTracker.Record("ldarg.2.ByRef", Ldarg2_ByRef(0, 0, ref refVal) == 99);

        // Instance method
        var obj = new TestClass();
        TestTracker.Record("ldarg.2.Instance", obj.GetArg2(0, 42) == 42);

        // Sum with previous args
        TestTracker.Record("ldarg.2.WithPrevious", Ldarg2_Sum(1, 2, 3) == 6);
    }

    private static int Ldarg2_SByte(int a, int b, sbyte arg2) => arg2;
    private static int Ldarg2_Byte(int a, int b, byte arg2) => arg2;
    private static int Ldarg2_Int16(int a, int b, short arg2) => arg2;
    private static int Ldarg2_UInt16(int a, int b, ushort arg2) => arg2;
    private static int Ldarg2_Int32(int a, int b, int arg2) => arg2;
    private static uint Ldarg2_UInt32(int a, int b, uint arg2) => arg2;
    private static long Ldarg2_Int64(int a, int b, long arg2) => arg2;
    private static ulong Ldarg2_UInt64(int a, int b, ulong arg2) => arg2;
    private static float Ldarg2_Float(int a, int b, float arg2) => arg2;
    private static double Ldarg2_Double(int a, int b, double arg2) => arg2;
    private static object? Ldarg2_Object(object? a, object? b, object? arg2) => arg2;
    private static SmallStruct Ldarg2_Struct(int a, int b, SmallStruct arg2) => arg2;
    private static int Ldarg2_ByRef(int a, int b, ref int arg2) => arg2;
    private static int Ldarg2_Sum(int a, int b, int c) => a + b + c;

    #endregion

    #region ldarg.3 (0x05) - 16 tests

    private static void TestLdarg3()
    {
        // Primitive types
        TestTracker.Record("ldarg.3.Int8", Ldarg3_SByte(0, 0, 0, (sbyte)-42) == -42);
        TestTracker.Record("ldarg.3.UInt8", Ldarg3_Byte(0, 0, 0, (byte)200) == 200);
        TestTracker.Record("ldarg.3.Int16", Ldarg3_Int16(0, 0, 0, (short)-1000) == -1000);
        TestTracker.Record("ldarg.3.UInt16", Ldarg3_UInt16(0, 0, 0, (ushort)50000) == 50000);
        TestTracker.Record("ldarg.3.Int32", Ldarg3_Int32(0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.3.UInt32", Ldarg3_UInt32(0, 0, 0, 3000000000u) == 3000000000u);
        TestTracker.Record("ldarg.3.Int64", Ldarg3_Int64(0, 0, 0, 0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.3.UInt64", Ldarg3_UInt64(0, 0, 0, 0xFEDCBA9876543210UL) == 0xFEDCBA9876543210UL);
        TestTracker.Record("ldarg.3.Float32", Assert.AreApproxEqual(3.14f, Ldarg3_Float(0, 0, 0, 3.14f)));
        TestTracker.Record("ldarg.3.Float64", Assert.AreApproxEqual(2.71828, Ldarg3_Double(0, 0, 0, 2.71828)));

        // Reference types
        TestTracker.Record("ldarg.3.Object", Ldarg3_Object(null, null, null, "test") == "test");
        TestTracker.Record("ldarg.3.Null", Ldarg3_Object("a", "b", "c", null) == null);

        // Value types
        var small = new SmallStruct { X = 50, Y = 60 };
        TestTracker.Record("ldarg.3.Struct", Ldarg3_Struct(0, 0, 0, small).X == 50);

        // Byref
        int refVal = 77;
        TestTracker.Record("ldarg.3.ByRef", Ldarg3_ByRef(0, 0, 0, ref refVal) == 77);

        // Instance method
        var obj = new TestClass();
        TestTracker.Record("ldarg.3.Instance", obj.GetArg3(0, 0, 42) == 42);

        // All different types
        TestTracker.Record("ldarg.3.AllTypes", Assert.AreApproxEqual(4.0, Ldarg3_AllTypes(1, 2L, 3.0f, 4.0)));
    }

    private static int Ldarg3_SByte(int a, int b, int c, sbyte arg3) => arg3;
    private static int Ldarg3_Byte(int a, int b, int c, byte arg3) => arg3;
    private static int Ldarg3_Int16(int a, int b, int c, short arg3) => arg3;
    private static int Ldarg3_UInt16(int a, int b, int c, ushort arg3) => arg3;
    private static int Ldarg3_Int32(int a, int b, int c, int arg3) => arg3;
    private static uint Ldarg3_UInt32(int a, int b, int c, uint arg3) => arg3;
    private static long Ldarg3_Int64(int a, int b, int c, long arg3) => arg3;
    private static ulong Ldarg3_UInt64(int a, int b, int c, ulong arg3) => arg3;
    private static float Ldarg3_Float(int a, int b, int c, float arg3) => arg3;
    private static double Ldarg3_Double(int a, int b, int c, double arg3) => arg3;
    private static object? Ldarg3_Object(object? a, object? b, object? c, object? arg3) => arg3;
    private static SmallStruct Ldarg3_Struct(int a, int b, int c, SmallStruct arg3) => arg3;
    private static int Ldarg3_ByRef(int a, int b, int c, ref int arg3) => arg3;
    private static double Ldarg3_AllTypes(int a, long b, float c, double d) => d;

    #endregion

    #region ldloc.0 (0x06) - 20 tests

    private static void TestLdloc0()
    {
        // Primitive types
        sbyte loc0_i8 = -42;
        TestTracker.Record("ldloc.0.Int8", loc0_i8 == -42);

        byte loc0_u8 = 200;
        TestTracker.Record("ldloc.0.UInt8", loc0_u8 == 200);

        short loc0_i16 = -1000;
        TestTracker.Record("ldloc.0.Int16", loc0_i16 == -1000);

        ushort loc0_u16 = 50000;
        TestTracker.Record("ldloc.0.UInt16", loc0_u16 == 50000);

        int loc0_i32 = 42;
        TestTracker.Record("ldloc.0.Int32", loc0_i32 == 42);

        uint loc0_u32 = 3000000000u;
        TestTracker.Record("ldloc.0.UInt32", loc0_u32 == 3000000000u);

        long loc0_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("ldloc.0.Int64", loc0_i64 == 0x123456789ABCDEF0L);

        ulong loc0_u64 = 0xFEDCBA9876543210UL;
        TestTracker.Record("ldloc.0.UInt64", loc0_u64 == 0xFEDCBA9876543210UL);

        float loc0_f32 = 3.14f;
        TestTracker.Record("ldloc.0.Float32", Assert.AreApproxEqual(3.14f, loc0_f32));

        double loc0_f64 = 2.71828;
        TestTracker.Record("ldloc.0.Float64", Assert.AreApproxEqual(2.71828, loc0_f64));

        bool loc0_bool = true;
        TestTracker.Record("ldloc.0.Bool", loc0_bool == true);

        char loc0_char = 'X';
        TestTracker.Record("ldloc.0.Char", loc0_char == 'X');

        // Reference types
        object loc0_obj = "test";
        TestTracker.Record("ldloc.0.Object", loc0_obj == "test");

        object? loc0_null = null;
        TestTracker.Record("ldloc.0.Null", loc0_null == null);

        int[] loc0_arr = new int[] { 1, 2, 3 };
        TestTracker.Record("ldloc.0.Array", loc0_arr.Length == 3);

        string loc0_str = "hello";
        TestTracker.Record("ldloc.0.String", loc0_str == "hello");

        // Value types
        SmallStruct loc0_small = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldloc.0.SmallStruct", loc0_small.X == 10 && loc0_small.Y == 20);

        LargeStruct loc0_large = new LargeStruct { A = 1, B = 2, C = 3 };
        TestTracker.Record("ldloc.0.LargeStruct", loc0_large.A == 1 && loc0_large.B == 2 && loc0_large.C == 3);

        StructWithRef loc0_swr = new StructWithRef { Value = 42, Ref = "ref" };
        TestTracker.Record("ldloc.0.StructWithRef", loc0_swr.Value == 42 && (string?)loc0_swr.Ref == "ref");

        // Test zero initialization
        int loc0_uninit = default;
        TestTracker.Record("ldloc.0.ZeroInit", loc0_uninit == 0);
    }

    #endregion

    #region ldloc.1 (0x07) - 14 tests

    private static void TestLdloc1()
    {
        int dummy0 = 0;

        int loc1_i32 = 42;
        TestTracker.Record("ldloc.1.Int32", loc1_i32 == 42);

        _ = dummy0;
        long loc1_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("ldloc.1.Int64", loc1_i64 == 0x123456789ABCDEF0L);

        int d1 = 0;
        float loc1_f32 = 3.14f;
        TestTracker.Record("ldloc.1.Float32", Assert.AreApproxEqual(3.14f, loc1_f32));
        _ = d1;

        int d2 = 0;
        double loc1_f64 = 2.71828;
        TestTracker.Record("ldloc.1.Float64", Assert.AreApproxEqual(2.71828, loc1_f64));
        _ = d2;

        int d3 = 0;
        object loc1_obj = "test";
        TestTracker.Record("ldloc.1.Object", loc1_obj == "test");
        _ = d3;

        int d4 = 0;
        object? loc1_null = null;
        TestTracker.Record("ldloc.1.Null", loc1_null == null);
        _ = d4;

        int d5 = 0;
        SmallStruct loc1_struct = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldloc.1.Struct", loc1_struct.X == 10);
        _ = d5;

        // Round-trip test
        int d6 = 0;
        int loc1_rt = 0;
        loc1_rt = 99;
        TestTracker.Record("ldloc.1.RoundTrip", loc1_rt == 99);
        _ = d6;

        // Zero init test
        int d7 = 0;
        int loc1_zero = default;
        TestTracker.Record("ldloc.1.ZeroInit", loc1_zero == 0);
        _ = d7;

        // Sign extension tests
        int d8 = 0;
        sbyte loc1_i8 = -42;
        TestTracker.Record("ldloc.1.Int8SignExt", loc1_i8 == -42);
        _ = d8;

        int d9 = 0;
        byte loc1_u8 = 200;
        TestTracker.Record("ldloc.1.UInt8ZeroExt", loc1_u8 == 200);
        _ = d9;

        int d10 = 0;
        short loc1_i16 = -1000;
        TestTracker.Record("ldloc.1.Int16SignExt", loc1_i16 == -1000);
        _ = d10;

        int d11 = 0;
        ushort loc1_u16 = 50000;
        TestTracker.Record("ldloc.1.UInt16ZeroExt", loc1_u16 == 50000);
        _ = d11;

        // After ldloc.0 test
        int a = 1;
        int b = 42;
        TestTracker.Record("ldloc.1.AfterLdloc0", a + b == 43);
    }

    #endregion

    #region ldloc.2 (0x08) - 14 tests

    private static void TestLdloc2()
    {
        int d0 = 0, d1 = 0;

        int loc2_i32 = 42;
        TestTracker.Record("ldloc.2.Int32", loc2_i32 == 42);
        _ = d0; _ = d1;

        int e0 = 0, e1 = 0;
        long loc2_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("ldloc.2.Int64", loc2_i64 == 0x123456789ABCDEF0L);
        _ = e0; _ = e1;

        int f0 = 0, f1 = 0;
        float loc2_f32 = 3.14f;
        TestTracker.Record("ldloc.2.Float32", Assert.AreApproxEqual(3.14f, loc2_f32));
        _ = f0; _ = f1;

        int g0 = 0, g1 = 0;
        double loc2_f64 = 2.71828;
        TestTracker.Record("ldloc.2.Float64", Assert.AreApproxEqual(2.71828, loc2_f64));
        _ = g0; _ = g1;

        int h0 = 0, h1 = 0;
        object loc2_obj = "test";
        TestTracker.Record("ldloc.2.Object", loc2_obj == "test");
        _ = h0; _ = h1;

        int i0 = 0, i1 = 0;
        object? loc2_null = null;
        TestTracker.Record("ldloc.2.Null", loc2_null == null);
        _ = i0; _ = i1;

        int j0 = 0, j1 = 0;
        SmallStruct loc2_struct = new SmallStruct { X = 30, Y = 40 };
        TestTracker.Record("ldloc.2.Struct", loc2_struct.Y == 40);
        _ = j0; _ = j1;

        // Round-trip
        int k0 = 0, k1 = 0;
        int loc2_rt = 0;
        loc2_rt = 77;
        TestTracker.Record("ldloc.2.RoundTrip", loc2_rt == 77);
        _ = k0; _ = k1;

        // Zero init
        int l0 = 0, l1 = 0;
        int loc2_zero = default;
        TestTracker.Record("ldloc.2.ZeroInit", loc2_zero == 0);
        _ = l0; _ = l1;

        // Sign/zero extension
        int m0 = 0, m1 = 0;
        sbyte loc2_i8 = -42;
        TestTracker.Record("ldloc.2.Int8SignExt", loc2_i8 == -42);
        _ = m0; _ = m1;

        int n0 = 0, n1 = 0;
        byte loc2_u8 = 200;
        TestTracker.Record("ldloc.2.UInt8ZeroExt", loc2_u8 == 200);
        _ = n0; _ = n1;

        int o0 = 0, o1 = 0;
        short loc2_i16 = -1000;
        TestTracker.Record("ldloc.2.Int16SignExt", loc2_i16 == -1000);
        _ = o0; _ = o1;

        int p0 = 0, p1 = 0;
        ushort loc2_u16 = 50000;
        TestTracker.Record("ldloc.2.UInt16ZeroExt", loc2_u16 == 50000);
        _ = p0; _ = p1;

        // Sum test
        int a = 1, b = 2, c = 42;
        TestTracker.Record("ldloc.2.Sum", a + b + c == 45);
    }

    #endregion

    #region ldloc.3 (0x09) - 14 tests

    private static void TestLdloc3()
    {
        int d0 = 0, d1 = 0, d2 = 0;

        int loc3_i32 = 42;
        TestTracker.Record("ldloc.3.Int32", loc3_i32 == 42);
        _ = d0; _ = d1; _ = d2;

        int e0 = 0, e1 = 0, e2 = 0;
        long loc3_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("ldloc.3.Int64", loc3_i64 == 0x123456789ABCDEF0L);
        _ = e0; _ = e1; _ = e2;

        int f0 = 0, f1 = 0, f2 = 0;
        float loc3_f32 = 3.14f;
        TestTracker.Record("ldloc.3.Float32", Assert.AreApproxEqual(3.14f, loc3_f32));
        _ = f0; _ = f1; _ = f2;

        int g0 = 0, g1 = 0, g2 = 0;
        double loc3_f64 = 2.71828;
        TestTracker.Record("ldloc.3.Float64", Assert.AreApproxEqual(2.71828, loc3_f64));
        _ = g0; _ = g1; _ = g2;

        int h0 = 0, h1 = 0, h2 = 0;
        object loc3_obj = "test";
        TestTracker.Record("ldloc.3.Object", loc3_obj == "test");
        _ = h0; _ = h1; _ = h2;

        int i0 = 0, i1 = 0, i2 = 0;
        object? loc3_null = null;
        TestTracker.Record("ldloc.3.Null", loc3_null == null);
        _ = i0; _ = i1; _ = i2;

        int j0 = 0, j1 = 0, j2 = 0;
        SmallStruct loc3_struct = new SmallStruct { X = 50, Y = 60 };
        TestTracker.Record("ldloc.3.Struct", loc3_struct.X == 50);
        _ = j0; _ = j1; _ = j2;

        // Round-trip
        int k0 = 0, k1 = 0, k2 = 0;
        int loc3_rt = 0;
        loc3_rt = 88;
        TestTracker.Record("ldloc.3.RoundTrip", loc3_rt == 88);
        _ = k0; _ = k1; _ = k2;

        // Zero init
        int l0 = 0, l1 = 0, l2 = 0;
        int loc3_zero = default;
        TestTracker.Record("ldloc.3.ZeroInit", loc3_zero == 0);
        _ = l0; _ = l1; _ = l2;

        // Sign/zero extension
        int m0 = 0, m1 = 0, m2 = 0;
        sbyte loc3_i8 = -42;
        TestTracker.Record("ldloc.3.Int8SignExt", loc3_i8 == -42);
        _ = m0; _ = m1; _ = m2;

        int n0 = 0, n1 = 0, n2 = 0;
        byte loc3_u8 = 200;
        TestTracker.Record("ldloc.3.UInt8ZeroExt", loc3_u8 == 200);
        _ = n0; _ = n1; _ = n2;

        int o0 = 0, o1 = 0, o2 = 0;
        short loc3_i16 = -1000;
        TestTracker.Record("ldloc.3.Int16SignExt", loc3_i16 == -1000);
        _ = o0; _ = o1; _ = o2;

        int p0 = 0, p1 = 0, p2 = 0;
        ushort loc3_u16 = 50000;
        TestTracker.Record("ldloc.3.UInt16ZeroExt", loc3_u16 == 50000);
        _ = p0; _ = p1; _ = p2;

        // Sum test
        int a = 1, b = 2, c = 3, d = 42;
        TestTracker.Record("ldloc.3.Sum", a + b + c + d == 48);
    }

    #endregion

    #region stloc.0 (0x0A) - 18 tests

    private static void TestStloc0()
    {
        // Primitive types
        int st0_i32;
        st0_i32 = 42;
        TestTracker.Record("stloc.0.Int32", st0_i32 == 42);

        long st0_i64;
        st0_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("stloc.0.Int64", st0_i64 == 0x123456789ABCDEF0L);

        float st0_f32;
        st0_f32 = 3.14f;
        TestTracker.Record("stloc.0.Float32", Assert.AreApproxEqual(3.14f, st0_f32));

        double st0_f64;
        st0_f64 = 2.71828;
        TestTracker.Record("stloc.0.Float64", Assert.AreApproxEqual(2.71828, st0_f64));

        // Truncation tests
        sbyte st0_i8;
        st0_i8 = (sbyte)(-42);
        TestTracker.Record("stloc.0.TruncateInt8", st0_i8 == -42);

        byte st0_u8;
        st0_u8 = (byte)200;
        TestTracker.Record("stloc.0.TruncateUInt8", st0_u8 == 200);

        short st0_i16;
        st0_i16 = (short)(-1000);
        TestTracker.Record("stloc.0.TruncateInt16", st0_i16 == -1000);

        ushort st0_u16;
        st0_u16 = (ushort)50000;
        TestTracker.Record("stloc.0.TruncateUInt16", st0_u16 == 50000);

        bool st0_bool;
        st0_bool = true;
        TestTracker.Record("stloc.0.Bool", st0_bool == true);

        // Reference types
        object st0_obj;
        st0_obj = "test";
        TestTracker.Record("stloc.0.Object", st0_obj == "test");

        object? st0_null;
        st0_null = null;
        TestTracker.Record("stloc.0.Null", st0_null == null);

        int[] st0_arr;
        st0_arr = new int[] { 1, 2, 3 };
        TestTracker.Record("stloc.0.Array", st0_arr.Length == 3);

        // Type compatibility - derived to base
        object st0_derived;
        st0_derived = "string is object";
        TestTracker.Record("stloc.0.DerivedToBase", st0_derived is string);

        // Value types
        SmallStruct st0_small;
        st0_small = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("stloc.0.SmallStruct", st0_small.X == 10 && st0_small.Y == 20);

        LargeStruct st0_large;
        st0_large = new LargeStruct { A = 1, B = 2, C = 3 };
        TestTracker.Record("stloc.0.LargeStruct", st0_large.A == 1 && st0_large.B == 2);

        StructWithRef st0_swr;
        st0_swr = new StructWithRef { Value = 42, Ref = "ref" };
        TestTracker.Record("stloc.0.StructWithRef", st0_swr.Value == 42);

        // Multiple stores - reassignment
        int st0_multi;
        st0_multi = 42;
        st0_multi = 100;
        TestTracker.Record("stloc.0.Reassign", st0_multi == 100);

        // Store and modify
        int st0_modify;
        st0_modify = 10;
        st0_modify = st0_modify + 5;
        TestTracker.Record("stloc.0.Modify", st0_modify == 15);
    }

    #endregion

    #region stloc.1 (0x0B) - 12 tests

    private static void TestStloc1()
    {
        int d0 = 0;

        int st1_i32;
        st1_i32 = 42;
        TestTracker.Record("stloc.1.Int32", st1_i32 == 42);
        _ = d0;

        int e0 = 0;
        long st1_i64;
        st1_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("stloc.1.Int64", st1_i64 == 0x123456789ABCDEF0L);
        _ = e0;

        int f0 = 0;
        float st1_f32;
        st1_f32 = 3.14f;
        TestTracker.Record("stloc.1.Float32", Assert.AreApproxEqual(3.14f, st1_f32));
        _ = f0;

        int g0 = 0;
        double st1_f64;
        st1_f64 = 2.71828;
        TestTracker.Record("stloc.1.Float64", Assert.AreApproxEqual(2.71828, st1_f64));
        _ = g0;

        int h0 = 0;
        object st1_obj;
        st1_obj = "test";
        TestTracker.Record("stloc.1.Object", st1_obj == "test");
        _ = h0;

        int i0 = 0;
        object? st1_null;
        st1_null = null;
        TestTracker.Record("stloc.1.Null", st1_null == null);
        _ = i0;

        int j0 = 0;
        SmallStruct st1_struct;
        st1_struct = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("stloc.1.Struct", st1_struct.X == 10);
        _ = j0;

        // Truncation
        int k0 = 0;
        sbyte st1_i8;
        st1_i8 = (sbyte)(-42);
        TestTracker.Record("stloc.1.TruncateInt8", st1_i8 == -42);
        _ = k0;

        int l0 = 0;
        short st1_i16;
        st1_i16 = (short)(-1000);
        TestTracker.Record("stloc.1.TruncateInt16", st1_i16 == -1000);
        _ = l0;

        // Round-trip
        int m0 = 0;
        int st1_rt;
        st1_rt = 99;
        int loaded = st1_rt;
        TestTracker.Record("stloc.1.RoundTrip", loaded == 99);
        _ = m0;

        // Multiple stores
        int n0 = 0;
        int st1_multi;
        st1_multi = 42;
        st1_multi = 100;
        TestTracker.Record("stloc.1.MultipleStores", st1_multi == 100);
        _ = n0;

        // Store from expression
        int o0 = 0;
        int st1_expr;
        st1_expr = 10 + 20 + 12;
        TestTracker.Record("stloc.1.FromExpression", st1_expr == 42);
        _ = o0;
    }

    #endregion

    #region stloc.2 (0x0C) - 12 tests

    private static void TestStloc2()
    {
        int d0 = 0, d1 = 0;

        int st2_i32;
        st2_i32 = 42;
        TestTracker.Record("stloc.2.Int32", st2_i32 == 42);
        _ = d0; _ = d1;

        int e0 = 0, e1 = 0;
        long st2_i64;
        st2_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("stloc.2.Int64", st2_i64 == 0x123456789ABCDEF0L);
        _ = e0; _ = e1;

        int f0 = 0, f1 = 0;
        float st2_f32;
        st2_f32 = 3.14f;
        TestTracker.Record("stloc.2.Float32", Assert.AreApproxEqual(3.14f, st2_f32));
        _ = f0; _ = f1;

        int g0 = 0, g1 = 0;
        double st2_f64;
        st2_f64 = 2.71828;
        TestTracker.Record("stloc.2.Float64", Assert.AreApproxEqual(2.71828, st2_f64));
        _ = g0; _ = g1;

        int h0 = 0, h1 = 0;
        object st2_obj;
        st2_obj = "test";
        TestTracker.Record("stloc.2.Object", st2_obj == "test");
        _ = h0; _ = h1;

        int i0 = 0, i1 = 0;
        object? st2_null;
        st2_null = null;
        TestTracker.Record("stloc.2.Null", st2_null == null);
        _ = i0; _ = i1;

        int j0 = 0, j1 = 0;
        SmallStruct st2_struct;
        st2_struct = new SmallStruct { X = 30, Y = 40 };
        TestTracker.Record("stloc.2.Struct", st2_struct.Y == 40);
        _ = j0; _ = j1;

        // Truncation
        int k0 = 0, k1 = 0;
        sbyte st2_i8;
        st2_i8 = (sbyte)(-42);
        TestTracker.Record("stloc.2.TruncateInt8", st2_i8 == -42);
        _ = k0; _ = k1;

        int l0 = 0, l1 = 0;
        short st2_i16;
        st2_i16 = (short)(-1000);
        TestTracker.Record("stloc.2.TruncateInt16", st2_i16 == -1000);
        _ = l0; _ = l1;

        // Round-trip
        int m0 = 0, m1 = 0;
        int st2_rt;
        st2_rt = 77;
        int loaded = st2_rt;
        TestTracker.Record("stloc.2.RoundTrip", loaded == 77);
        _ = m0; _ = m1;

        // Multiple stores
        int n0 = 0, n1 = 0;
        int st2_multi;
        st2_multi = 42;
        st2_multi = 100;
        TestTracker.Record("stloc.2.MultipleStores", st2_multi == 100);
        _ = n0; _ = n1;

        // Store from expression
        int o0 = 0, o1 = 0;
        int st2_expr;
        st2_expr = 10 + 20 + 12;
        TestTracker.Record("stloc.2.FromExpression", st2_expr == 42);
        _ = o0; _ = o1;
    }

    #endregion

    #region stloc.3 (0x0D) - 12 tests

    private static void TestStloc3()
    {
        int d0 = 0, d1 = 0, d2 = 0;

        int st3_i32;
        st3_i32 = 42;
        TestTracker.Record("stloc.3.Int32", st3_i32 == 42);
        _ = d0; _ = d1; _ = d2;

        int e0 = 0, e1 = 0, e2 = 0;
        long st3_i64;
        st3_i64 = 0x123456789ABCDEF0L;
        TestTracker.Record("stloc.3.Int64", st3_i64 == 0x123456789ABCDEF0L);
        _ = e0; _ = e1; _ = e2;

        int f0 = 0, f1 = 0, f2 = 0;
        float st3_f32;
        st3_f32 = 3.14f;
        TestTracker.Record("stloc.3.Float32", Assert.AreApproxEqual(3.14f, st3_f32));
        _ = f0; _ = f1; _ = f2;

        int g0 = 0, g1 = 0, g2 = 0;
        double st3_f64;
        st3_f64 = 2.71828;
        TestTracker.Record("stloc.3.Float64", Assert.AreApproxEqual(2.71828, st3_f64));
        _ = g0; _ = g1; _ = g2;

        int h0 = 0, h1 = 0, h2 = 0;
        object st3_obj;
        st3_obj = "test";
        TestTracker.Record("stloc.3.Object", st3_obj == "test");
        _ = h0; _ = h1; _ = h2;

        int i0 = 0, i1 = 0, i2 = 0;
        object? st3_null;
        st3_null = null;
        TestTracker.Record("stloc.3.Null", st3_null == null);
        _ = i0; _ = i1; _ = i2;

        int j0 = 0, j1 = 0, j2 = 0;
        SmallStruct st3_struct;
        st3_struct = new SmallStruct { X = 50, Y = 60 };
        TestTracker.Record("stloc.3.Struct", st3_struct.X == 50);
        _ = j0; _ = j1; _ = j2;

        // Truncation
        int k0 = 0, k1 = 0, k2 = 0;
        sbyte st3_i8;
        st3_i8 = (sbyte)(-42);
        TestTracker.Record("stloc.3.TruncateInt8", st3_i8 == -42);
        _ = k0; _ = k1; _ = k2;

        int l0 = 0, l1 = 0, l2 = 0;
        short st3_i16;
        st3_i16 = (short)(-1000);
        TestTracker.Record("stloc.3.TruncateInt16", st3_i16 == -1000);
        _ = l0; _ = l1; _ = l2;

        // Round-trip
        int m0 = 0, m1 = 0, m2 = 0;
        int st3_rt;
        st3_rt = 88;
        int loaded = st3_rt;
        TestTracker.Record("stloc.3.RoundTrip", loaded == 88);
        _ = m0; _ = m1; _ = m2;

        // Multiple stores
        int n0 = 0, n1 = 0, n2 = 0;
        int st3_multi;
        st3_multi = 42;
        st3_multi = 100;
        TestTracker.Record("stloc.3.MultipleStores", st3_multi == 100);
        _ = n0; _ = n1; _ = n2;

        // Store from expression
        int o0 = 0, o1 = 0, o2 = 0;
        int st3_expr;
        st3_expr = 10 + 20 + 12;
        TestTracker.Record("stloc.3.FromExpression", st3_expr == 42);
        _ = o0; _ = o1; _ = o2;
    }

    #endregion
}
