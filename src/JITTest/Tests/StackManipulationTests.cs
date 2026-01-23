// JITTest - Stack Manipulation Tests
// Section 4: dup (0x25), pop (0x26)

namespace JITTest;

/// <summary>
/// Tests for stack manipulation instructions (ECMA-335 Section III.3.38, III.3.50)
/// </summary>
public static class StackManipulationTests
{
    // Test struct (small, fits in register)
    private struct SmallStruct
    {
        public int Value;
    }

    // Test struct (large, >8 bytes)
    private struct LargeStruct
    {
        public long A;
        public long B;
    }

    public static void RunAll()
    {
        TestDup();
        TestPop();
        TestDupAdditional();
        TestPopAdditional();
    }

    #region dup (0x25)

    private static void TestDup()
    {
        // Primitive types
        TestTracker.Record("dup.i4.Basic", TestDupInt32() == 42);
        TestTracker.Record("dup.i4.Zero", TestDupInt32Zero() == 0);
        TestTracker.Record("dup.i4.Negative", TestDupInt32Neg() == -123);
        TestTracker.Record("dup.i4.Max", TestDupInt32Max() == int.MaxValue);
        TestTracker.Record("dup.i4.Min", TestDupInt32Min() == int.MinValue);

        TestTracker.Record("dup.i8.Basic", TestDupInt64() == 0x123456789ABCDEF0L);
        TestTracker.Record("dup.i8.Zero", TestDupInt64Zero() == 0L);
        TestTracker.Record("dup.i8.Negative", TestDupInt64Neg() == -9876543210L);
        TestTracker.Record("dup.i8.Max", TestDupInt64Max() == long.MaxValue);
        TestTracker.Record("dup.i8.Min", TestDupInt64Min() == long.MinValue);

        TestTracker.Record("dup.r4.Basic", Assert.AreApproxEqual(3.14f, TestDupFloat()));
        TestTracker.Record("dup.r4.Zero", Assert.AreApproxEqual(0.0f, TestDupFloatZero()));
        TestTracker.Record("dup.r4.Negative", Assert.AreApproxEqual(-2.5f, TestDupFloatNeg()));

        TestTracker.Record("dup.r8.Basic", Assert.AreApproxEqual(3.14159, TestDupDouble(), 1e-5));
        TestTracker.Record("dup.r8.Zero", Assert.AreApproxEqual(0.0, TestDupDoubleZero()));
        TestTracker.Record("dup.r8.Negative", Assert.AreApproxEqual(-123.456, TestDupDoubleNeg(), 1e-5));

        // Reference types
        TestTracker.Record("dup.Object.SameRef", TestDupObject());
        TestTracker.Record("dup.Null", TestDupNull());
        TestTracker.Record("dup.String.SameRef", TestDupString());
        TestTracker.Record("dup.Array.SameRef", TestDupArray());

        // Usage patterns
        TestTracker.Record("dup.UsesBoth.Add", TestDupUsesBothAdd() == 84);
        TestTracker.Record("dup.UsesBoth.Mul", TestDupUsesBothMul() == 1764);
        TestTracker.Record("dup.StoreAndUse", TestDupStoreAndUse() == 42);
        TestTracker.Record("dup.CompareAndUse", TestDupCompareAndUse());
        TestTracker.Record("dup.MultipleDup", TestMultipleDup() == 168);
        TestTracker.Record("dup.DupThenPop", TestDupThenPop() == 42);

        // Independence verification
        TestTracker.Record("dup.i4.IndepCopy", TestDupIndependent() == 42);
        TestTracker.Record("dup.i4.BothEqual", TestDupBothEqual());
    }

    // Primitive type dup tests
    private static int TestDupInt32()
    {
        int x = 42;
        int y = x;
        return y;
    }

    private static int TestDupInt32Zero()
    {
        int x = 0;
        int y = x;
        return y;
    }

    private static int TestDupInt32Neg()
    {
        int x = -123;
        int y = x;
        return y;
    }

    private static int TestDupInt32Max()
    {
        int x = int.MaxValue;
        int y = x;
        return y;
    }

    private static int TestDupInt32Min()
    {
        int x = int.MinValue;
        int y = x;
        return y;
    }

    private static long TestDupInt64()
    {
        long x = 0x123456789ABCDEF0L;
        long y = x;
        return y;
    }

    private static long TestDupInt64Zero()
    {
        long x = 0L;
        long y = x;
        return y;
    }

    private static long TestDupInt64Neg()
    {
        long x = -9876543210L;
        long y = x;
        return y;
    }

    private static long TestDupInt64Max()
    {
        long x = long.MaxValue;
        long y = x;
        return y;
    }

    private static long TestDupInt64Min()
    {
        long x = long.MinValue;
        long y = x;
        return y;
    }

    private static float TestDupFloat()
    {
        float x = 3.14f;
        float y = x;
        return y;
    }

    private static float TestDupFloatZero()
    {
        float x = 0.0f;
        float y = x;
        return y;
    }

    private static float TestDupFloatNeg()
    {
        float x = -2.5f;
        float y = x;
        return y;
    }

    private static double TestDupDouble()
    {
        double x = 3.14159;
        double y = x;
        return y;
    }

    private static double TestDupDoubleZero()
    {
        double x = 0.0;
        double y = x;
        return y;
    }

    private static double TestDupDoubleNeg()
    {
        double x = -123.456;
        double y = x;
        return y;
    }

    // Reference type dup tests
    private static bool TestDupObject()
    {
        object obj = "test";
        object copy = obj;
        return ReferenceEquals(obj, copy);
    }

    private static bool TestDupNull()
    {
        object? obj = null;
        object? copy = obj;
        return copy == null;
    }

    private static bool TestDupString()
    {
        string s = "hello";
        string copy = s;
        return ReferenceEquals(s, copy);
    }

    private static bool TestDupArray()
    {
        int[] arr = new int[] { 1, 2, 3 };
        int[] copy = arr;
        return ReferenceEquals(arr, copy);
    }

    // Usage pattern tests
    private static int TestDupUsesBothAdd()
    {
        int x = 42;
        return x + x;
    }

    private static int TestDupUsesBothMul()
    {
        int x = 42;
        return x * x;
    }

    private static int TestDupStoreAndUse()
    {
        int x = GetValue42();
        int y = x;  // dup; stloc pattern
        return y;
    }

    private static bool TestDupCompareAndUse()
    {
        int x = 42;
        int y = x;  // dup
        return x == y;  // compare copies
    }

    private static int TestMultipleDup()
    {
        int x = 42;
        int a = x;
        int b = x;
        int c = x;
        int d = x;
        return a + b + c + d;  // 42 * 4 = 168
    }

    private static int TestDupThenPop()
    {
        int x = 42;
        int y = x;  // dup
        _ = y;      // pop the dup
        return x;   // original still valid
    }

    // Independence and equality tests
    private static int TestDupIndependent()
    {
        int x = 42;
        int y = x;  // copy
        y = 100;    // modify copy
        return x;   // original unchanged
    }

    private static bool TestDupBothEqual()
    {
        int x = 42;
        int y = x;
        return x == y;
    }

    private static int GetValue42() => 42;

    #endregion

    #region pop (0x26)

    private static void TestPop()
    {
        // Primitive types
        TestTracker.Record("pop.i4.Basic", TestPopInt32() == 42);
        TestTracker.Record("pop.i4.Zero", TestPopInt32Zero() == 0);
        TestTracker.Record("pop.i8.Basic", TestPopInt64() == 100L);
        TestTracker.Record("pop.r4.Basic", Assert.AreApproxEqual(3.14f, TestPopFloat()));
        TestTracker.Record("pop.r8.Basic", Assert.AreApproxEqual(2.718, TestPopDouble(), 1e-5));

        // Reference types
        TestTracker.Record("pop.Object", TestPopObject() == 42);
        TestTracker.Record("pop.Null", TestPopNull());
        TestTracker.Record("pop.String", TestPopString() == 42);
        TestTracker.Record("pop.Array", TestPopArray() == 42);

        // Usage patterns
        TestTracker.Record("pop.DiscardReturn", TestPopDiscardReturn() == 100);
        TestTracker.Record("pop.DiscardVoid", TestPopDiscardVoid() == 50);
        TestTracker.Record("pop.Multiple", TestPopMultiple() == 5);
        TestTracker.Record("pop.AfterDup", TestPopAfterDup() == 42);
        TestTracker.Record("pop.UnusedLocal", TestPopUnusedLocal() == 99);
    }

    // Primitive type pop tests
    private static int TestPopInt32()
    {
        int x = 10;
        int y = 42;
        _ = x;  // pop x
        return y;
    }

    private static int TestPopInt32Zero()
    {
        int x = 999;
        int y = 0;
        _ = x;  // pop x
        return y;
    }

    private static long TestPopInt64()
    {
        long x = 999L;
        long y = 100L;
        _ = x;
        return y;
    }

    private static float TestPopFloat()
    {
        float x = 999.0f;
        float y = 3.14f;
        _ = x;
        return y;
    }

    private static double TestPopDouble()
    {
        double x = 999.999;
        double y = 2.718;
        _ = x;
        return y;
    }

    // Reference type pop tests
    private static int TestPopObject()
    {
        object obj = "discarded";
        int result = 42;
        _ = obj;
        return result;
    }

    private static bool TestPopNull()
    {
        object? obj = null;
        _ = obj;
        return true;
    }

    private static int TestPopString()
    {
        string s = "discarded";
        int result = 42;
        _ = s;
        return result;
    }

    private static int TestPopArray()
    {
        int[] arr = new int[] { 1, 2, 3 };
        int result = 42;
        _ = arr;
        return result;
    }

    // Usage pattern tests
    private static int TestPopDiscardReturn()
    {
        GetValue999();  // return value popped
        return 100;
    }

    private static int TestPopDiscardVoid()
    {
        DoNothing();  // no return value, but pattern test
        return 50;
    }

    private static int TestPopMultiple()
    {
        int a = 1, b = 2, c = 3, d = 4, e = 5;
        _ = a;
        _ = b;
        _ = c;
        _ = d;
        return e;
    }

    private static int TestPopAfterDup()
    {
        int x = 42;
        int y = x;  // dup
        _ = y;      // pop the dup
        return x;   // original remains
    }

    private static int TestPopUnusedLocal()
    {
        int unused = GetValue999();
        _ = unused;
        return 99;
    }

    private static int GetValue999() => 999;
    private static void DoNothing() { }

    #endregion

    #region Additional dup tests

    private static void TestDupAdditional()
    {
        // Byte type
        TestTracker.Record("dup.byte.Zero", TestDupByteZero() == 0);
        TestTracker.Record("dup.byte.One", TestDupByteOne() == 1);
        TestTracker.Record("dup.byte.Max", TestDupByteMax() == 255);
        TestTracker.Record("dup.byte.Mid", TestDupByteMid() == 128);

        // SByte type
        TestTracker.Record("dup.sbyte.Zero", TestDupSByteZero() == 0);
        TestTracker.Record("dup.sbyte.Pos", TestDupSBytePos() == 100);
        TestTracker.Record("dup.sbyte.Max", TestDupSByteMax() == 127);
        TestTracker.Record("dup.sbyte.Min", TestDupSByteMin() == -128);

        // Short type
        TestTracker.Record("dup.short.Zero", TestDupShortZero() == 0);
        TestTracker.Record("dup.short.Pos", TestDupShortPos() == 1000);
        TestTracker.Record("dup.short.Max", TestDupShortMax() == short.MaxValue);
        TestTracker.Record("dup.short.Min", TestDupShortMin() == short.MinValue);

        // UShort type
        TestTracker.Record("dup.ushort.Zero", TestDupUShortZero() == 0);
        TestTracker.Record("dup.ushort.One", TestDupUShortOne() == 1);
        TestTracker.Record("dup.ushort.Max", TestDupUShortMax() == ushort.MaxValue);
        TestTracker.Record("dup.ushort.Mid", TestDupUShortMid() == 30000);

        // UInt type
        TestTracker.Record("dup.uint.Zero", TestDupUIntZero() == 0u);
        TestTracker.Record("dup.uint.One", TestDupUIntOne() == 1u);
        TestTracker.Record("dup.uint.Large", TestDupUIntLarge() == 0x7FFFFFFFu);
        TestTracker.Record("dup.uint.Mid", TestDupUIntMid() == 1000000u);

        // ULong type
        TestTracker.Record("dup.ulong.Zero", TestDupULongZero() == 0UL);
        TestTracker.Record("dup.ulong.One", TestDupULongOne() == 1UL);
        TestTracker.Record("dup.ulong.Large", TestDupULongLarge() == 0x7FFFFFFFFFFFFFFFUL);
        TestTracker.Record("dup.ulong.Pattern", TestDupULongPattern() == 0xDEADBEEFCAFEBABEUL);

        // Char type
        TestTracker.Record("dup.char.A", TestDupCharA() == 'A');
        TestTracker.Record("dup.char.Zero", TestDupCharZero() == '\0');
        TestTracker.Record("dup.char.Space", TestDupCharSpace() == ' ');
        TestTracker.Record("dup.char.Z", TestDupCharZ() == 'Z');

        // Bool type
        TestTracker.Record("dup.bool.True", TestDupBoolTrue() == true);
        TestTracker.Record("dup.bool.False", TestDupBoolFalse() == false);
    }

    private static byte TestDupByteZero() { byte x = 0; byte y = x; return y; }
    private static byte TestDupByteOne() { byte x = 1; byte y = x; return y; }
    private static byte TestDupByteMax() { byte x = 255; byte y = x; return y; }
    private static byte TestDupByteMid() { byte x = 128; byte y = x; return y; }

    private static sbyte TestDupSByteZero() { sbyte x = 0; sbyte y = x; return y; }
    private static sbyte TestDupSBytePos() { sbyte x = 100; sbyte y = x; return y; }
    private static sbyte TestDupSByteMax() { sbyte x = 127; sbyte y = x; return y; }
    private static sbyte TestDupSByteMin() { sbyte x = -128; sbyte y = x; return y; }

    private static short TestDupShortZero() { short x = 0; short y = x; return y; }
    private static short TestDupShortPos() { short x = 1000; short y = x; return y; }
    private static short TestDupShortMax() { short x = short.MaxValue; short y = x; return y; }
    private static short TestDupShortMin() { short x = short.MinValue; short y = x; return y; }

    private static ushort TestDupUShortZero() { ushort x = 0; ushort y = x; return y; }
    private static ushort TestDupUShortOne() { ushort x = 1; ushort y = x; return y; }
    private static ushort TestDupUShortMax() { ushort x = ushort.MaxValue; ushort y = x; return y; }
    private static ushort TestDupUShortMid() { ushort x = 30000; ushort y = x; return y; }

    private static uint TestDupUIntZero() { uint x = 0u; uint y = x; return y; }
    private static uint TestDupUIntOne() { uint x = 1u; uint y = x; return y; }
    private static uint TestDupUIntLarge() { uint x = 0x7FFFFFFFu; uint y = x; return y; }
    private static uint TestDupUIntMid() { uint x = 1000000u; uint y = x; return y; }

    private static ulong TestDupULongZero() { ulong x = 0UL; ulong y = x; return y; }
    private static ulong TestDupULongOne() { ulong x = 1UL; ulong y = x; return y; }
    private static ulong TestDupULongLarge() { ulong x = 0x7FFFFFFFFFFFFFFFUL; ulong y = x; return y; }
    private static ulong TestDupULongPattern() { ulong x = 0xDEADBEEFCAFEBABEUL; ulong y = x; return y; }

    private static char TestDupCharA() { char x = 'A'; char y = x; return y; }
    private static char TestDupCharZero() { char x = '\0'; char y = x; return y; }
    private static char TestDupCharSpace() { char x = ' '; char y = x; return y; }
    private static char TestDupCharZ() { char x = 'Z'; char y = x; return y; }

    private static bool TestDupBoolTrue() { bool x = true; bool y = x; return y; }
    private static bool TestDupBoolFalse() { bool x = false; bool y = x; return y; }

    #endregion

    #region Additional pop tests

    private static void TestPopAdditional()
    {
        // Byte type
        TestTracker.Record("pop.byte.Zero", TestPopByteZero());
        TestTracker.Record("pop.byte.Max", TestPopByteMax());

        // Short type
        TestTracker.Record("pop.short.Zero", TestPopShortZero());
        TestTracker.Record("pop.short.Max", TestPopShortMax());

        // UInt type
        TestTracker.Record("pop.uint.Zero", TestPopUIntZero());
        TestTracker.Record("pop.uint.Large", TestPopUIntLarge());

        // ULong type
        TestTracker.Record("pop.ulong.Zero", TestPopULongZero());
        TestTracker.Record("pop.ulong.Large", TestPopULongLarge());

        // Char type
        TestTracker.Record("pop.char.A", TestPopCharA());
        TestTracker.Record("pop.char.Zero", TestPopCharZero());

        // Bool type
        TestTracker.Record("pop.bool.True", TestPopBoolTrue());
        TestTracker.Record("pop.bool.False", TestPopBoolFalse());
    }

    private static bool TestPopByteZero() { byte x = 0; _ = x; return true; }
    private static bool TestPopByteMax() { byte x = 255; _ = x; return true; }
    private static bool TestPopShortZero() { short x = 0; _ = x; return true; }
    private static bool TestPopShortMax() { short x = short.MaxValue; _ = x; return true; }
    private static bool TestPopUIntZero() { uint x = 0u; _ = x; return true; }
    private static bool TestPopUIntLarge() { uint x = 0x7FFFFFFFu; _ = x; return true; }
    private static bool TestPopULongZero() { ulong x = 0UL; _ = x; return true; }
    private static bool TestPopULongLarge() { ulong x = 0x7FFFFFFFFFFFFFFFUL; _ = x; return true; }
    private static bool TestPopCharA() { char x = 'A'; _ = x; return true; }
    private static bool TestPopCharZero() { char x = '\0'; _ = x; return true; }
    private static bool TestPopBoolTrue() { bool x = true; _ = x; return true; }
    private static bool TestPopBoolFalse() { bool x = false; _ = x; return true; }

    #endregion
}
