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
        // ldarg.s tests (22 tests)
        TestLdargS();

        // ldarga.s tests (18 tests)
        TestLdargaS();

        // starg.s tests (20 tests)
        TestStargS();

        // ldloc.s tests (20 tests)
        TestLdlocS();

        // ldloca.s tests (18 tests)
        TestLdlocaS();

        // stloc.s tests (14 tests)
        TestStlocS();
    }

    // Helper structs for testing
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

    // Helper class for instance method tests
    private class TestClass
    {
        public int Value;

        public int GetArg4(int a, int b, int c, int arg4) => arg4;
        public int GetArg5(int a, int b, int c, int d, int arg5) => arg5;
    }

    private struct TestStruct
    {
        public int Value;

        public int GetThisValue() => Value;
        public int GetArg4(int a, int b, int c, int arg4) => arg4;
    }

    #region ldarg.s (0x0E) - 22 tests

    private static void TestLdargS()
    {
        // Index range tests
        TestTracker.Record("ldarg.s.Index4", LdargS_Arg4(0, 0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.s.Index5", LdargS_Arg5(0, 0, 0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.s.Index6", LdargS_Arg6(0, 0, 0, 0, 0, 0, 42) == 42);

        // Type coverage
        TestTracker.Record("ldarg.s.Int8", LdargS_SByte(0, 0, 0, 0, (sbyte)-42) == -42);
        TestTracker.Record("ldarg.s.UInt8", LdargS_Byte(0, 0, 0, 0, (byte)200) == 200);
        TestTracker.Record("ldarg.s.Int16", LdargS_Int16(0, 0, 0, 0, (short)-1000) == -1000);
        TestTracker.Record("ldarg.s.UInt16", LdargS_UInt16(0, 0, 0, 0, (ushort)50000) == 50000);
        TestTracker.Record("ldarg.s.Int32", LdargS_Int32(0, 0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.s.Int64", LdargS_Int64(0, 0, 0, 0, 0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarg.s.Float32", Assert.AreApproxEqual(3.14f, LdargS_Float(0, 0, 0, 0, 3.14f)));
        TestTracker.Record("ldarg.s.Float64", Assert.AreApproxEqual(2.71828, LdargS_Double(0, 0, 0, 0, 2.71828)));
        TestTracker.Record("ldarg.s.Object", LdargS_Object(null, null, null, null, "test") != null);
        TestTracker.Record("ldarg.s.Null", LdargS_Object(null, null, null, null, null) == null);

        // Value types
        var small = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldarg.s.SmallStruct", LdargS_SmallStruct(0, 0, 0, 0, small).X == 10);

        // Large struct (24 bytes) at arg index 4 - passed by pointer on stack
        var large = new LargeStruct { A = 1, B = 2, C = 3 };

        // First test: void method with large struct arg - no hidden buffer
        var testResult = LdargS_LargeStruct_ReadOnly(0, 0, 0, 0, large);
        TestTracker.Record("ldarg.s.LargeStructReadOnly", testResult == 2);

        // Second test: method returning large struct (has hidden buffer)
        var result = LdargS_LargeStruct(0, 0, 0, 0, large);
        TestTracker.Record("ldarg.s.LargeStruct", result.B == 2);

        // Byref
        int refVal = 42;
        TestTracker.Record("ldarg.s.ByRef", LdargS_ByRef(0, 0, 0, 0, ref refVal) == 42);

        // Instance method context
        var obj = new TestClass { Value = 0 };
        TestTracker.Record("ldarg.s.InstanceArg4", obj.GetArg4(0, 0, 0, 42) == 42);
        TestTracker.Record("ldarg.s.InstanceArg5", obj.GetArg5(0, 0, 0, 0, 42) == 42);

        var structVal = new TestStruct { Value = 0 };
        TestTracker.Record("ldarg.s.StructInstanceArg4", structVal.GetArg4(0, 0, 0, 42) == 42);

        // Load same arg multiple times
        TestTracker.Record("ldarg.s.LoadMultiple", LdargS_LoadTwice(0, 0, 0, 0, 21) == 42);

        // Interleave with other operations
        TestTracker.Record("ldarg.s.Interleave", LdargS_Interleave(1, 2, 3, 4, 5) == 15);
    }

    private static int LdargS_Arg4(int a, int b, int c, int d, int e) => e;
    private static int LdargS_Arg5(int a, int b, int c, int d, int e, int f) => f;
    private static int LdargS_Arg6(int a, int b, int c, int d, int e, int f, int g) => g;
    private static int LdargS_SByte(int a, int b, int c, int d, sbyte e) => e;
    private static int LdargS_Byte(int a, int b, int c, int d, byte e) => e;
    private static int LdargS_Int16(int a, int b, int c, int d, short e) => e;
    private static int LdargS_UInt16(int a, int b, int c, int d, ushort e) => e;
    private static int LdargS_Int32(int a, int b, int c, int d, int e) => e;
    private static long LdargS_Int64(int a, int b, int c, int d, long e) => e;
    private static float LdargS_Float(int a, int b, int c, int d, float e) => e;
    private static double LdargS_Double(int a, int b, int c, int d, double e) => e;
    private static object? LdargS_Object(object? a, object? b, object? c, object? d, object? e) => e;
    private static SmallStruct LdargS_SmallStruct(int a, int b, int c, int d, SmallStruct e) => e;
    private static LargeStruct LdargS_LargeStruct(int a, int b, int c, int d, LargeStruct e) => e;
    // This method returns just B field - no hidden buffer, tests if large struct arg is passed correctly
    private static long LdargS_LargeStruct_ReadOnly(int a, int b, int c, int d, LargeStruct e) => e.B;
    private static int LdargS_ByRef(int a, int b, int c, int d, ref int e) => e;
    private static int LdargS_LoadTwice(int a, int b, int c, int d, int e) => e + e;
    private static int LdargS_Interleave(int a, int b, int c, int d, int e) => a + b + c + d + e;

    #endregion

    #region ldarga.s (0x0F) - 18 tests

    private static void TestLdargaS()
    {
        // Basic addressing
        TestTracker.Record("ldarga.s.Int32", LdargaS_Int32(42) == 42);
        TestTracker.Record("ldarga.s.Int64", LdargaS_Int64(0x123456789ABCDEF0L) == 0x123456789ABCDEF0L);
        TestTracker.Record("ldarga.s.Float32", Assert.AreApproxEqual(3.14f, LdargaS_Float(3.14f)));
        TestTracker.Record("ldarga.s.Float64", Assert.AreApproxEqual(2.71828, LdargaS_Double(2.71828)));

        var small = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldarga.s.Struct", LdargaS_Struct(small) == 10);

        object objRef = "test";
        TestTracker.Record("ldarga.s.Object", LdargaS_Object(objRef));

        // Index range (arg 4 and beyond use ldarga.s)
        TestTracker.Record("ldarga.s.Arg4", LdargaS_Arg4(0, 0, 0, 0, 42) == 42);
        TestTracker.Record("ldarga.s.Arg5", LdargaS_Arg5(0, 0, 0, 0, 0, 42) == 42);

        // Usage patterns - modify through address
        int modifyVal = 10;
        LdargaS_ModifyViaPtr(ref modifyVal);
        TestTracker.Record("ldarga.s.ModifyViaPtr", modifyVal == 42);

        // Read via address
        TestTracker.Record("ldarga.s.ReadViaPtr", LdargaS_ReadViaPtr(100) == 100);

        // Instance struct method - address of 'this'
        var testStruct = new TestStruct { Value = 42 };
        TestTracker.Record("ldarga.s.ThisAddress", testStruct.GetThisValue() == 42);

        // Arg after 'this' in instance method
        var obj = new TestClass();
        TestTracker.Record("ldarga.s.ArgAfterThis", obj.GetArg4(0, 0, 0, 99) == 99);

        // Load full struct via address
        TestTracker.Record("ldarga.s.LoadObj", LdargaS_LoadObj(small).Y == 20);

        // Store via address
        TestTracker.Record("ldarga.s.StoreObj", LdargaS_StoreObj(small) == 100);

        // Multiple operations with same address
        TestTracker.Record("ldarga.s.MultipleOps", LdargaS_MultipleOps(10) == 20);

        // Test with int8/int16 widening
        TestTracker.Record("ldarga.s.Int8Addr", LdargaS_SByte((sbyte)-42) == -42);
        TestTracker.Record("ldarga.s.Int16Addr", LdargaS_Int16((short)-1000) == -1000);
    }

    private static unsafe int LdargaS_Int32(int arg)
    {
        int* ptr = &arg;
        return *ptr;
    }

    private static unsafe long LdargaS_Int64(long arg)
    {
        long* ptr = &arg;
        return *ptr;
    }

    private static unsafe float LdargaS_Float(float arg)
    {
        float* ptr = &arg;
        return *ptr;
    }

    private static unsafe double LdargaS_Double(double arg)
    {
        double* ptr = &arg;
        return *ptr;
    }

    private static unsafe int LdargaS_Struct(SmallStruct arg)
    {
        SmallStruct* ptr = &arg;
        return ptr->X;
    }

    private static unsafe bool LdargaS_Object(object arg)
    {
        // Get address of reference slot
        fixed (char* c = ((string)arg))
        {
            return c != null;
        }
    }

    private static unsafe int LdargaS_Arg4(int a, int b, int c, int d, int e)
    {
        int* ptr = &e;
        return *ptr;
    }

    private static unsafe int LdargaS_Arg5(int a, int b, int c, int d, int e, int f)
    {
        int* ptr = &f;
        return *ptr;
    }

    private static unsafe void LdargaS_ModifyViaPtr(ref int arg)
    {
        fixed (int* ptr = &arg)
        {
            *ptr = 42;
        }
    }

    private static unsafe int LdargaS_ReadViaPtr(int arg)
    {
        int* ptr = &arg;
        int val = *ptr;
        return val;
    }

    private static unsafe SmallStruct LdargaS_LoadObj(SmallStruct arg)
    {
        SmallStruct* ptr = &arg;
        return *ptr;
    }

    private static unsafe int LdargaS_StoreObj(SmallStruct arg)
    {
        SmallStruct* ptr = &arg;
        *ptr = new SmallStruct { X = 100, Y = 200 };
        return arg.X;
    }

    private static unsafe int LdargaS_MultipleOps(int arg)
    {
        int* ptr = &arg;
        *ptr = *ptr + 5;
        *ptr = *ptr + 5;
        return *ptr;
    }

    private static unsafe int LdargaS_SByte(sbyte arg)
    {
        sbyte* ptr = &arg;
        return *ptr;
    }

    private static unsafe int LdargaS_Int16(short arg)
    {
        short* ptr = &arg;
        return *ptr;
    }

    #endregion

    #region starg.s (0x10) - 20 tests

    private static void TestStargS()
    {
        // Type coverage
        TestTracker.Record("starg.s.Int32", StargS_Int32(0) == 42);
        TestTracker.Record("starg.s.Int64", StargS_Int64(0) == 0x123456789ABCDEF0L);
        TestTracker.Record("starg.s.Float32", Assert.AreApproxEqual(3.14f, StargS_Float(0)));
        TestTracker.Record("starg.s.Float64", Assert.AreApproxEqual(2.71828, StargS_Double(0)));
        TestTracker.Record("starg.s.Object", StargS_Object(null) != null);
        TestTracker.Record("starg.s.Null", StargS_Null("test") == null);

        var small = new SmallStruct { X = 0, Y = 0 };
        TestTracker.Record("starg.s.Struct", StargS_Struct(small).X == 100);

        TestTracker.Record("starg.s.Int8", StargS_SByte((sbyte)0) == -42);
        TestTracker.Record("starg.s.Int16", StargS_Int16((short)0) == -1000);

        // Index range (arg 4 and beyond)
        TestTracker.Record("starg.s.Arg4", StargS_Arg4(0, 0, 0, 0, 0) == 42);
        TestTracker.Record("starg.s.Arg5", StargS_Arg5(0, 0, 0, 0, 0, 0) == 42);

        // Method contexts - static
        TestTracker.Record("starg.s.Static", StargS_Static(0) == 42);

        // Round-trip
        TestTracker.Record("starg.s.RoundTrip", StargS_RoundTrip(0) == 99);

        // Multiple stores
        TestTracker.Record("starg.s.MultipleStores", StargS_Multiple(0) == 30);

        // Store doesn't affect caller
        int callerVal = 10;
        StargS_NoCallerEffect(callerVal);
        TestTracker.Record("starg.s.NoCallerEffect", callerVal == 10);

        // Derived to base (object)
        TestTracker.Record("starg.s.DerivedToBase", StargS_DerivedToBase(null));

        // Interleave stores
        TestTracker.Record("starg.s.Interleave", StargS_Interleave(1, 2, 3, 4, 5) == 50);

        // UInt types
        TestTracker.Record("starg.s.UInt32", StargS_UInt32(0) == 3000000000u);
        TestTracker.Record("starg.s.UInt64", StargS_UInt64(0) == 0xFEDCBA9876543210UL);
    }

    private static int StargS_Int32(int arg) { arg = 42; return arg; }
    private static long StargS_Int64(long arg) { arg = 0x123456789ABCDEF0L; return arg; }
    private static float StargS_Float(float arg) { arg = 3.14f; return arg; }
    private static double StargS_Double(double arg) { arg = 2.71828; return arg; }
    private static object StargS_Object(object? arg) { arg = "assigned"; return arg; }
    private static object? StargS_Null(object? arg) { arg = null; return arg; }
    private static SmallStruct StargS_Struct(SmallStruct arg) { arg = new SmallStruct { X = 100, Y = 200 }; return arg; }
    private static int StargS_SByte(sbyte arg) { arg = -42; return arg; }
    private static int StargS_Int16(short arg) { arg = -1000; return arg; }
    private static int StargS_Arg4(int a, int b, int c, int d, int e) { e = 42; return e; }
    private static int StargS_Arg5(int a, int b, int c, int d, int e, int f) { f = 42; return f; }
    private static int StargS_Static(int arg) { arg = 42; return arg; }
    private static int StargS_RoundTrip(int arg) { arg = 99; int loaded = arg; return loaded; }
    private static int StargS_Multiple(int arg) { arg = 10; arg = 20; arg = 30; return arg; }
    private static void StargS_NoCallerEffect(int arg) { arg = 999; }
    private static bool StargS_DerivedToBase(object? arg) { arg = "string is object"; return arg is string; }
    private static int StargS_Interleave(int a, int b, int c, int d, int e)
    {
        a = 10; b = 10; c = 10; d = 10; e = 10;
        return a + b + c + d + e;
    }
    private static uint StargS_UInt32(uint arg) { arg = 3000000000u; return arg; }
    private static ulong StargS_UInt64(ulong arg) { arg = 0xFEDCBA9876543210UL; return arg; }

    #endregion

    #region ldloc.s (0x11) - 20 tests

    private static void TestLdlocS()
    {
        // Index range - local 4 and beyond use ldloc.s
        {
            int l0 = 0, l1 = 0, l2 = 0, l3 = 0;
            int l4 = 42;
            TestTracker.Record("ldloc.s.Index4", l4 == 42);
            _ = l0; _ = l1; _ = l2; _ = l3;
        }

        {
            int l0 = 0, l1 = 0, l2 = 0, l3 = 0, l4 = 0;
            int l5 = 42;
            TestTracker.Record("ldloc.s.Index5", l5 == 42);
            _ = l0; _ = l1; _ = l2; _ = l3; _ = l4;
        }

        {
            int l0 = 0, l1 = 0, l2 = 0, l3 = 0, l4 = 0, l5 = 0;
            int l6 = 42;
            TestTracker.Record("ldloc.s.Index6", l6 == 42);
            _ = l0; _ = l1; _ = l2; _ = l3; _ = l4; _ = l5;
        }

        // Type coverage
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            sbyte l4 = -42;
            TestTracker.Record("ldloc.s.Int8", l4 == -42);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            byte l4 = 200;
            TestTracker.Record("ldloc.s.UInt8", l4 == 200);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            short l4 = -1000;
            TestTracker.Record("ldloc.s.Int16", l4 == -1000);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            ushort l4 = 50000;
            TestTracker.Record("ldloc.s.UInt16", l4 == 50000);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 42;
            TestTracker.Record("ldloc.s.Int32", l4 == 42);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            long l4 = 0x123456789ABCDEF0L;
            TestTracker.Record("ldloc.s.Int64", l4 == 0x123456789ABCDEF0L);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            float l4 = 3.14f;
            TestTracker.Record("ldloc.s.Float32", Assert.AreApproxEqual(3.14f, l4));
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            double l4 = 2.71828;
            TestTracker.Record("ldloc.s.Float64", Assert.AreApproxEqual(2.71828, l4));
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            object l4 = "test";
            TestTracker.Record("ldloc.s.Object", l4 != null);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            object? l4 = null;
            TestTracker.Record("ldloc.s.Null", l4 == null);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            SmallStruct l4 = new SmallStruct { X = 10, Y = 20 };
            TestTracker.Record("ldloc.s.SmallStruct", l4.X == 10);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Large struct at local index 4+
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            LargeStruct l4 = new LargeStruct { A = 1, B = 2, C = 3 };
            TestTracker.Record("ldloc.s.LargeStruct", l4.B == 2);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            bool l4 = true;
            TestTracker.Record("ldloc.s.Bool", l4 == true);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            char l4 = 'X';
            TestTracker.Record("ldloc.s.Char", l4 == 'X');
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Zero init
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = default;
            TestTracker.Record("ldloc.s.ZeroInit", l4 == 0);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Round-trip
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 0;
            l4 = 99;
            TestTracker.Record("ldloc.s.RoundTrip", l4 == 99);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }
    }

    #endregion

    #region ldloca.s (0x12) - 18 tests

    private static void TestLdlocaS()
    {
        // Basic addressing
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 42;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { int* ptr = &l4; TestTracker.Record("ldloca.s.Int32", *ptr == 42); }
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            long l4 = 0x123456789ABCDEF0L;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { long* ptr = &l4; TestTracker.Record("ldloca.s.Int64", *ptr == 0x123456789ABCDEF0L); }
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            float l4 = 3.14f;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { float* ptr = &l4; TestTracker.Record("ldloca.s.Float32", Assert.AreApproxEqual(3.14f, *ptr)); }
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            double l4 = 2.71828;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { double* ptr = &l4; TestTracker.Record("ldloca.s.Float64", Assert.AreApproxEqual(2.71828, *ptr)); }
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            SmallStruct l4 = new SmallStruct { X = 10, Y = 20 };
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { SmallStruct* ptr = &l4; TestTracker.Record("ldloca.s.Struct", ptr->X == 10); }
        }

        // Index range
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 42;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { int* ptr = &l4; TestTracker.Record("ldloca.s.Index4", *ptr == 42); }
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0, d4 = 0;
            int l5 = 42;
            _ = d0; _ = d1; _ = d2; _ = d3; _ = d4;
            unsafe { int* ptr = &l5; TestTracker.Record("ldloca.s.Index5", *ptr == 42); }
        }

        // Modify via pointer
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 10;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { int* ptr = &l4; *ptr = 42; }
            TestTracker.Record("ldloca.s.ModifyViaPtr", l4 == 42);
        }

        // Read via pointer
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 99;
            _ = d0; _ = d1; _ = d2; _ = d3;
            int readVal;
            unsafe { int* ptr = &l4; readVal = *ptr; }
            TestTracker.Record("ldloca.s.ReadViaPtr", readVal == 99);
        }

        // Load full struct via address
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            SmallStruct l4 = new SmallStruct { X = 100, Y = 200 };
            _ = d0; _ = d1; _ = d2; _ = d3;
            SmallStruct copy;
            unsafe { SmallStruct* ptr = &l4; copy = *ptr; }
            TestTracker.Record("ldloca.s.LoadObj", copy.Y == 200);
        }

        // Store via address
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            SmallStruct l4 = new SmallStruct { X = 0, Y = 0 };
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { SmallStruct* ptr = &l4; *ptr = new SmallStruct { X = 50, Y = 60 }; }
            TestTracker.Record("ldloca.s.StoreObj", l4.X == 50);
        }

        // Multiple operations
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 10;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { int* ptr = &l4; *ptr += 5; *ptr += 5; }
            TestTracker.Record("ldloca.s.MultipleOps", l4 == 20);
        }

        // Int8/Int16 widening
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            sbyte l4 = -42;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { sbyte* ptr = &l4; TestTracker.Record("ldloca.s.Int8Addr", *ptr == -42); }
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            short l4 = -1000;
            _ = d0; _ = d1; _ = d2; _ = d3;
            unsafe { short* ptr = &l4; TestTracker.Record("ldloca.s.Int16Addr", *ptr == -1000); }
        }

        // Use for ref param call
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 10;
            _ = d0; _ = d1; _ = d2; _ = d3;
            LdlocaS_RefHelper(ref l4);
            TestTracker.Record("ldloca.s.RefParam", l4 == 42);
        }

        // Use for out param call
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4;
            _ = d0; _ = d1; _ = d2; _ = d3;
            LdlocaS_OutHelper(out l4);
            TestTracker.Record("ldloca.s.OutParam", l4 == 99);
        }

        // Pointer arithmetic
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4 = 42;
            _ = d0; _ = d1; _ = d2; _ = d3;
            bool valid;
            unsafe { int* ptr = &l4; valid = (nint)ptr != 0; }
            TestTracker.Record("ldloca.s.PtrArithmetic", valid);
        }
    }

    private static void LdlocaS_RefHelper(ref int val) { val = 42; }
    private static void LdlocaS_OutHelper(out int val) { val = 99; }

    #endregion

    #region stloc.s (0x13) - 14 tests

    private static void TestStlocS()
    {
        // Basic store
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4;
            l4 = 42;
            TestTracker.Record("stloc.s.Int32", l4 == 42);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            long l4;
            l4 = 0x123456789ABCDEF0L;
            TestTracker.Record("stloc.s.Int64", l4 == 0x123456789ABCDEF0L);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            float l4;
            l4 = 3.14f;
            TestTracker.Record("stloc.s.Float32", Assert.AreApproxEqual(3.14f, l4));
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            double l4;
            l4 = 2.71828;
            TestTracker.Record("stloc.s.Float64", Assert.AreApproxEqual(2.71828, l4));
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            object l4;
            l4 = "test";
            TestTracker.Record("stloc.s.Object", l4 != null);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            object? l4;
            l4 = null;
            TestTracker.Record("stloc.s.Null", l4 == null);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            SmallStruct l4;
            l4 = new SmallStruct { X = 10, Y = 20 };
            TestTracker.Record("stloc.s.Struct", l4.X == 10);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Truncation
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            sbyte l4;
            l4 = -42;
            TestTracker.Record("stloc.s.TruncateInt8", l4 == -42);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            short l4;
            l4 = -1000;
            TestTracker.Record("stloc.s.TruncateInt16", l4 == -1000);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Reassign
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4;
            l4 = 42;
            l4 = 100;
            TestTracker.Record("stloc.s.Reassign", l4 == 100);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Multiple stores
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4;
            l4 = 10;
            l4 = 20;
            l4 = 30;
            TestTracker.Record("stloc.s.MultipleStores", l4 == 30);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // From expression
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0;
            int l4;
            l4 = 10 + 20 + 12;
            TestTracker.Record("stloc.s.FromExpression", l4 == 42);
            _ = d0; _ = d1; _ = d2; _ = d3;
        }

        // Index 5
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0, d4 = 0;
            int l5;
            l5 = 42;
            TestTracker.Record("stloc.s.Index5", l5 == 42);
            _ = d0; _ = d1; _ = d2; _ = d3; _ = d4;
        }

        // Index 6
        {
            int d0 = 0, d1 = 0, d2 = 0, d3 = 0, d4 = 0, d5 = 0;
            int l6;
            l6 = 42;
            TestTracker.Record("stloc.s.Index6", l6 == 42);
            _ = d0; _ = d1; _ = d2; _ = d3; _ = d4; _ = d5;
        }
    }

    #endregion
}
