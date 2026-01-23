// JITTest - Field Operation Tests
// Section 17: ldfld, stfld, ldsfld, stsfld, ldflda, ldsflda

namespace JITTest;

/// <summary>
/// Tests for field operation instructions
/// </summary>
public static class FieldOperationTests
{
    // Static fields for testing
    private static sbyte s_staticSByte;
    private static byte s_staticByte;
    private static short s_staticShort;
    private static ushort s_staticUShort;
    private static int s_staticInt;
    private static uint s_staticUInt;
    private static long s_staticLong;
    private static ulong s_staticULong;
    private static float s_staticFloat;
    private static double s_staticDouble;
    private static bool s_staticBool;
    private static char s_staticChar;
    private static string? s_staticString;
    private static object? s_staticObject;
    private static int[]? s_staticArray;
    private static SmallStruct s_staticSmallStruct;
    private static LargeStruct s_staticLargeStruct;

    public static void RunAll()
    {
        TestLdfldPrimitives();
        TestLdfldReferences();
        TestLdfldValueTypes();
        TestLdfldSpecial();
        TestStfldPrimitives();
        TestStfldReferences();
        TestStfldValueTypes();
        TestLdsfldPrimitives();
        TestLdsfldReferences();
        TestLdsfldValueTypes();
        TestStsfldPrimitives();
        TestStsfldReferences();
        TestStsfldValueTypes();
        TestLdflda();
        TestLdsflda();

        // Additional field tests
        RunAdditionalTests();
    }

    #region Helper Types

    private class AllFieldsClass
    {
        public sbyte SByteField;
        public byte ByteField;
        public short ShortField;
        public ushort UShortField;
        public int IntField;
        public uint UIntField;
        public long LongField;
        public ulong ULongField;
        public float FloatField;
        public double DoubleField;
        public bool BoolField;
        public char CharField;
        public string? StringField;
        public object? ObjectField;
        public int[]? ArrayField;
        public SmallStruct SmallStructField;
        public LargeStruct LargeStructField;
        public NestedStruct NestedStructField;
    }

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
        public long D;
    }

    private struct NestedStruct
    {
        public SmallStruct Inner;
        public int Z;
    }

    private struct AllFieldsStruct
    {
        public int IntField;
        public long LongField;
        public SmallStruct StructField;
    }

    private class DerivedClass : AllFieldsClass
    {
        public int DerivedField;
    }

    #endregion

    #region ldfld (0x7B) - Primitive Fields

    private static void TestLdfldPrimitives()
    {
        var obj = new AllFieldsClass();

        // Test sign extension for small signed types
        obj.SByteField = -42;
        TestTracker.Record("ldfld.SByte", obj.SByteField == -42);

        obj.ByteField = 200;
        TestTracker.Record("ldfld.Byte", obj.ByteField == 200);

        obj.ShortField = -1000;
        TestTracker.Record("ldfld.Short", obj.ShortField == -1000);

        obj.UShortField = 50000;
        TestTracker.Record("ldfld.UShort", obj.UShortField == 50000);

        obj.IntField = 42;
        TestTracker.Record("ldfld.Int32", obj.IntField == 42);

        obj.UIntField = 3000000000u;
        TestTracker.Record("ldfld.UInt32", obj.UIntField == 3000000000u);

        obj.LongField = 0x123456789ABCDEF0L;
        TestTracker.Record("ldfld.Int64", obj.LongField == 0x123456789ABCDEF0L);

        obj.ULongField = 0xFEDCBA9876543210UL;
        TestTracker.Record("ldfld.UInt64", obj.ULongField == 0xFEDCBA9876543210UL);

        obj.FloatField = 3.14f;
        TestTracker.Record("ldfld.Float", obj.FloatField == 3.14f);

        obj.DoubleField = 2.71828;
        TestTracker.Record("ldfld.Double", obj.DoubleField == 2.71828);

        obj.BoolField = true;
        TestTracker.Record("ldfld.Bool", obj.BoolField == true);

        obj.CharField = 'X';
        TestTracker.Record("ldfld.Char", obj.CharField == 'X');
    }

    #endregion

    #region ldfld - Reference Fields

    private static void TestLdfldReferences()
    {
        var obj = new AllFieldsClass();

        obj.StringField = "test";
        TestTracker.Record("ldfld.String", obj.StringField == "test");

        obj.StringField = null;
        TestTracker.Record("ldfld.StringNull", obj.StringField == null);

        obj.ObjectField = "object";
        TestTracker.Record("ldfld.Object", (string?)obj.ObjectField == "object");

        obj.ObjectField = null;
        TestTracker.Record("ldfld.ObjectNull", obj.ObjectField == null);

        obj.ArrayField = new int[] { 1, 2, 3 };
        TestTracker.Record("ldfld.Array", obj.ArrayField != null && obj.ArrayField.Length == 3);

        obj.ArrayField = null;
        TestTracker.Record("ldfld.ArrayNull", obj.ArrayField == null);
    }

    #endregion

    #region ldfld - Value Type Fields

    private static void TestLdfldValueTypes()
    {
        var obj = new AllFieldsClass();

        // Small struct
        obj.SmallStructField = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldfld.SmallStruct", obj.SmallStructField.X == 10 && obj.SmallStructField.Y == 20);

        // Large struct
        obj.LargeStructField = new LargeStruct { A = 1, B = 2, C = 3, D = 4 };
        TestTracker.Record("ldfld.LargeStruct", obj.LargeStructField.A == 1 && obj.LargeStructField.D == 4);

        // Nested struct
        obj.NestedStructField = new NestedStruct { Inner = new SmallStruct { X = 5, Y = 6 }, Z = 7 };
        TestTracker.Record("ldfld.NestedStruct", obj.NestedStructField.Inner.X == 5 && obj.NestedStructField.Z == 7);

        // Load from struct receiver
        var structObj = new AllFieldsStruct { IntField = 42 };
        TestTracker.Record("ldfld.FromStruct", structObj.IntField == 42);
    }

    #endregion

    #region ldfld - Special Cases

    private static void TestLdfldSpecial()
    {
        // Inherited field
        var derived = new DerivedClass();
        derived.IntField = 42;
        derived.DerivedField = 100;
        TestTracker.Record("ldfld.Inherited", derived.IntField == 42);
        TestTracker.Record("ldfld.Derived", derived.DerivedField == 100);

        // Load through base reference
        AllFieldsClass baseRef = derived;
        TestTracker.Record("ldfld.ThroughBase", baseRef.IntField == 42);

        // Edge values
        var obj = new AllFieldsClass();
        obj.IntField = int.MaxValue;
        TestTracker.Record("ldfld.IntMax", obj.IntField == int.MaxValue);

        obj.IntField = int.MinValue;
        TestTracker.Record("ldfld.IntMin", obj.IntField == int.MinValue);
    }

    #endregion

    #region stfld (0x7D) - Primitive Fields

    private static void TestStfldPrimitives()
    {
        var obj = new AllFieldsClass();

        obj.SByteField = -42;
        TestTracker.Record("stfld.SByte", obj.SByteField == -42);

        obj.ByteField = 200;
        TestTracker.Record("stfld.Byte", obj.ByteField == 200);

        obj.ShortField = -1000;
        TestTracker.Record("stfld.Short", obj.ShortField == -1000);

        obj.UShortField = 50000;
        TestTracker.Record("stfld.UShort", obj.UShortField == 50000);

        obj.IntField = 42;
        TestTracker.Record("stfld.Int32", obj.IntField == 42);

        obj.UIntField = 3000000000u;
        TestTracker.Record("stfld.UInt32", obj.UIntField == 3000000000u);

        obj.LongField = 0x123456789ABCDEF0L;
        TestTracker.Record("stfld.Int64", obj.LongField == 0x123456789ABCDEF0L);

        obj.ULongField = 0xFEDCBA9876543210UL;
        TestTracker.Record("stfld.UInt64", obj.ULongField == 0xFEDCBA9876543210UL);

        obj.FloatField = 3.14f;
        TestTracker.Record("stfld.Float", obj.FloatField == 3.14f);

        obj.DoubleField = 2.71828;
        TestTracker.Record("stfld.Double", obj.DoubleField == 2.71828);

        obj.BoolField = true;
        TestTracker.Record("stfld.Bool", obj.BoolField == true);

        obj.CharField = 'Y';
        TestTracker.Record("stfld.Char", obj.CharField == 'Y');

        // Reassignment
        obj.IntField = 100;
        obj.IntField = 200;
        TestTracker.Record("stfld.Reassign", obj.IntField == 200);
    }

    #endregion

    #region stfld - Reference Fields

    private static void TestStfldReferences()
    {
        var obj = new AllFieldsClass();

        obj.StringField = "test";
        TestTracker.Record("stfld.String", obj.StringField == "test");

        obj.StringField = null;
        TestTracker.Record("stfld.StringToNull", obj.StringField == null);

        obj.ObjectField = "object";
        TestTracker.Record("stfld.Object", (string?)obj.ObjectField == "object");

        // Store derived to base field (covariance)
        obj.ObjectField = "derived string";
        TestTracker.Record("stfld.DerivedToBase", obj.ObjectField is string);

        obj.ArrayField = new int[] { 1, 2, 3 };
        TestTracker.Record("stfld.Array", obj.ArrayField != null && obj.ArrayField.Length == 3);
    }

    #endregion

    #region stfld - Value Type Fields

    private static void TestStfldValueTypes()
    {
        var obj = new AllFieldsClass();

        // Small struct
        obj.SmallStructField = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("stfld.SmallStruct", obj.SmallStructField.X == 10 && obj.SmallStructField.Y == 20);

        // Large struct
        obj.LargeStructField = new LargeStruct { A = 1, B = 2, C = 3, D = 4 };
        TestTracker.Record("stfld.LargeStruct", obj.LargeStructField.A == 1 && obj.LargeStructField.D == 4);

        // Nested struct
        obj.NestedStructField = new NestedStruct { Inner = new SmallStruct { X = 5, Y = 6 }, Z = 7 };
        TestTracker.Record("stfld.NestedStruct", obj.NestedStructField.Inner.X == 5);

        // Store to struct receiver
        var structObj = new AllFieldsStruct();
        structObj.IntField = 42;
        TestTracker.Record("stfld.ToStruct", structObj.IntField == 42);

        // Overwrite struct field
        obj.SmallStructField = new SmallStruct { X = 100, Y = 200 };
        TestTracker.Record("stfld.OverwriteStruct", obj.SmallStructField.X == 100);
    }

    #endregion

    #region ldsfld (0x7E) - Primitive Fields

    private static void TestLdsfldPrimitives()
    {
        s_staticSByte = -42;
        TestTracker.Record("ldsfld.SByte", s_staticSByte == -42);

        s_staticByte = 200;
        TestTracker.Record("ldsfld.Byte", s_staticByte == 200);

        s_staticShort = -1000;
        TestTracker.Record("ldsfld.Short", s_staticShort == -1000);

        s_staticUShort = 50000;
        TestTracker.Record("ldsfld.UShort", s_staticUShort == 50000);

        s_staticInt = 42;
        TestTracker.Record("ldsfld.Int32", s_staticInt == 42);

        s_staticUInt = 42u;
        TestTracker.Record("ldsfld.UInt32", s_staticUInt == 42u);

        // Test large UInt32 values (above 0x7FFFFFFF, which would be negative if signed)
        s_staticUInt = 3000000000u;  // 0xB2D05E00
        TestTracker.Record("ldsfld.UInt32Large", s_staticUInt == 3000000000u);

        s_staticUInt = 0xFFFFFFFFu;  // max UInt32
        TestTracker.Record("ldsfld.UInt32Max", s_staticUInt == 0xFFFFFFFFu);

        s_staticLong = 0x123456789ABCDEF0L;
        TestTracker.Record("ldsfld.Int64", s_staticLong == 0x123456789ABCDEF0L);

        s_staticULong = 0xFEDCBA9876543210UL;
        TestTracker.Record("ldsfld.UInt64", s_staticULong == 0xFEDCBA9876543210UL);

        s_staticFloat = 3.14f;
        TestTracker.Record("ldsfld.Float", s_staticFloat == 3.14f);

        s_staticDouble = 2.71828;
        TestTracker.Record("ldsfld.Double", s_staticDouble == 2.71828);

        s_staticBool = true;
        TestTracker.Record("ldsfld.Bool", s_staticBool == true);

        s_staticChar = 'Z';
        TestTracker.Record("ldsfld.Char", s_staticChar == 'Z');
    }

    #endregion

    #region ldsfld - Reference Fields

    private static void TestLdsfldReferences()
    {
        s_staticString = "test";
        TestTracker.Record("ldsfld.String", s_staticString == "test");

        s_staticString = null;
        TestTracker.Record("ldsfld.StringNull", s_staticString == null);

        s_staticObject = "object";
        TestTracker.Record("ldsfld.Object", (string?)s_staticObject == "object");

        s_staticArray = new int[] { 1, 2, 3 };
        TestTracker.Record("ldsfld.Array", s_staticArray != null && s_staticArray.Length == 3);
    }

    #endregion

    #region ldsfld - Value Type Fields

    private static void TestLdsfldValueTypes()
    {
        s_staticSmallStruct = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("ldsfld.SmallStruct", s_staticSmallStruct.X == 10 && s_staticSmallStruct.Y == 20);

        s_staticLargeStruct = new LargeStruct { A = 1, B = 2, C = 3, D = 4 };
        TestTracker.Record("ldsfld.LargeStruct", s_staticLargeStruct.A == 1 &&
            s_staticLargeStruct.B == 2 && s_staticLargeStruct.C == 3 && s_staticLargeStruct.D == 4);
    }

    #endregion

    #region stsfld (0x80) - Primitive Fields

    private static void TestStsfldPrimitives()
    {
        s_staticSByte = -42;
        TestTracker.Record("stsfld.SByte", s_staticSByte == -42);

        s_staticByte = 200;
        TestTracker.Record("stsfld.Byte", s_staticByte == 200);

        s_staticShort = -1000;
        TestTracker.Record("stsfld.Short", s_staticShort == -1000);

        s_staticUShort = 50000;
        TestTracker.Record("stsfld.UShort", s_staticUShort == 50000);

        s_staticInt = 42;
        TestTracker.Record("stsfld.Int32", s_staticInt == 42);

        s_staticUInt = 42u;
        TestTracker.Record("stsfld.UInt32", s_staticUInt == 42u);

        // Test large UInt32 values (above 0x7FFFFFFF, which would be negative if signed)
        s_staticUInt = 3000000000u;  // 0xB2D05E00
        TestTracker.Record("stsfld.UInt32Large", s_staticUInt == 3000000000u);

        s_staticUInt = 0xFFFFFFFFu;  // max UInt32
        TestTracker.Record("stsfld.UInt32Max", s_staticUInt == 0xFFFFFFFFu);

        s_staticLong = 0x123456789ABCDEF0L;
        TestTracker.Record("stsfld.Int64", s_staticLong == 0x123456789ABCDEF0L);

        s_staticFloat = 3.14f;
        TestTracker.Record("stsfld.Float", s_staticFloat == 3.14f);

        s_staticDouble = 2.71828;
        TestTracker.Record("stsfld.Double", s_staticDouble == 2.71828);

        s_staticBool = false;
        s_staticBool = true;
        TestTracker.Record("stsfld.Bool", s_staticBool == true);

        s_staticChar = 'A';
        TestTracker.Record("stsfld.Char", s_staticChar == 'A');

        // Reassignment
        s_staticInt = 100;
        s_staticInt = 200;
        TestTracker.Record("stsfld.Reassign", s_staticInt == 200);
    }

    #endregion

    #region stsfld - Reference Fields

    private static void TestStsfldReferences()
    {
        s_staticString = "test";
        TestTracker.Record("stsfld.String", s_staticString == "test");

        s_staticString = null;
        TestTracker.Record("stsfld.StringToNull", s_staticString == null);

        s_staticObject = "object";
        TestTracker.Record("stsfld.Object", (string?)s_staticObject == "object");

        s_staticArray = new int[] { 1, 2, 3 };
        TestTracker.Record("stsfld.Array", s_staticArray != null && s_staticArray.Length == 3);
    }

    #endregion

    #region stsfld - Value Type Fields

    private static void TestStsfldValueTypes()
    {
        s_staticSmallStruct = new SmallStruct { X = 10, Y = 20 };
        TestTracker.Record("stsfld.SmallStruct", s_staticSmallStruct.X == 10 && s_staticSmallStruct.Y == 20);

        s_staticLargeStruct = new LargeStruct { A = 1, B = 2, C = 3, D = 4 };
        TestTracker.Record("stsfld.LargeStruct", s_staticLargeStruct.A == 1 &&
            s_staticLargeStruct.B == 2 && s_staticLargeStruct.C == 3 && s_staticLargeStruct.D == 4);

        // Overwrite
        s_staticSmallStruct = new SmallStruct { X = 100, Y = 200 };
        TestTracker.Record("stsfld.OverwriteStruct", s_staticSmallStruct.X == 100);
    }

    #endregion

    #region ldflda (0x7C)

    private static unsafe void TestLdflda()
    {
        var obj = new AllFieldsClass { IntField = 42 };

        // Read via address
        fixed (int* ptr = &obj.IntField)
        {
            TestTracker.Record("ldflda.Read", *ptr == 42);
        }

        // Write via address
        fixed (int* ptr = &obj.IntField)
        {
            *ptr = 100;
        }
        TestTracker.Record("ldflda.Write", obj.IntField == 100);

        // Address of long field
        obj.LongField = 0x123456789ABCDEF0L;
        fixed (long* ptr = &obj.LongField)
        {
            TestTracker.Record("ldflda.Long", *ptr == 0x123456789ABCDEF0L);
        }

        // Address of struct field
        obj.SmallStructField = new SmallStruct { X = 10, Y = 20 };
        fixed (SmallStruct* ptr = &obj.SmallStructField)
        {
            TestTracker.Record("ldflda.Struct", ptr->X == 10);
            ptr->X = 50;
        }
        TestTracker.Record("ldflda.StructWrite", obj.SmallStructField.X == 50);

        // Ref parameter pattern
        obj.IntField = 77;
        IncrementByRef(ref obj.IntField);
        TestTracker.Record("ldflda.RefParam", obj.IntField == 78);
    }

    private static void IncrementByRef(ref int value) => value++;

    #endregion

    #region ldsflda (0x7F)

    private static unsafe void TestLdsflda()
    {
        // Read via address
        s_staticInt = 42;
        fixed (int* ptr = &s_staticInt)
        {
            TestTracker.Record("ldsflda.Read", *ptr == 42);
        }

        // Write via address
        fixed (int* ptr = &s_staticInt)
        {
            *ptr = 100;
        }
        TestTracker.Record("ldsflda.Write", s_staticInt == 100);

        // Address of long static field
        s_staticLong = 0x123456789ABCDEF0L;
        fixed (long* ptr = &s_staticLong)
        {
            TestTracker.Record("ldsflda.Long", *ptr == 0x123456789ABCDEF0L);
        }

        // Address of struct static field
        s_staticSmallStruct = new SmallStruct { X = 10, Y = 20 };
        fixed (SmallStruct* ptr = &s_staticSmallStruct)
        {
            TestTracker.Record("ldsflda.Struct", ptr->X == 10);
            ptr->X = 50;
        }
        TestTracker.Record("ldsflda.StructWrite", s_staticSmallStruct.X == 50);

        // Ref parameter pattern
        s_staticInt = 77;
        IncrementByRef(ref s_staticInt);
        TestTracker.Record("ldsflda.RefParam", s_staticInt == 78);
    }

    #endregion

    #region Additional Field Tests

    public static void RunAdditionalTests()
    {
        TestFieldReadModifyWrite();
        TestFieldChaining();
        TestStaticFieldSequence();
        TestFieldWithExpressions();
        TestNestedFieldAccess();
    }

    private static void TestFieldReadModifyWrite()
    {
        var obj = new AllFieldsClass();

        // Increment
        obj.IntField = 10;
        obj.IntField = obj.IntField + 1;
        TestTracker.Record("field.rmw.Inc", obj.IntField == 11);

        // Decrement
        obj.IntField = obj.IntField - 1;
        TestTracker.Record("field.rmw.Dec", obj.IntField == 10);

        // Multiply
        obj.IntField = obj.IntField * 5;
        TestTracker.Record("field.rmw.Mul", obj.IntField == 50);

        // Divide
        obj.IntField = obj.IntField / 2;
        TestTracker.Record("field.rmw.Div", obj.IntField == 25);

        // Long field RMW
        obj.LongField = 1000L;
        obj.LongField = obj.LongField * 1000L;
        TestTracker.Record("field.rmw.Long", obj.LongField == 1000000L);

        // Float field RMW
        obj.FloatField = 10.0f;
        obj.FloatField = obj.FloatField * 2.5f;
        TestTracker.Record("field.rmw.Float", obj.FloatField == 25.0f);

        // Double field RMW
        obj.DoubleField = 100.0;
        obj.DoubleField = obj.DoubleField / 4.0;
        TestTracker.Record("field.rmw.Double", obj.DoubleField == 25.0);
    }

    private static void TestFieldChaining()
    {
        var obj = new AllFieldsClass();

        // Multiple field operations in sequence
        obj.IntField = 10;
        obj.LongField = obj.IntField * 100L;
        TestTracker.Record("field.chain.IntToLong", obj.LongField == 1000L);

        obj.FloatField = obj.IntField * 0.5f;
        TestTracker.Record("field.chain.IntToFloat", obj.FloatField == 5.0f);

        obj.DoubleField = obj.LongField / 10.0;
        TestTracker.Record("field.chain.LongToDouble", obj.DoubleField == 100.0);

        // Copy between fields
        obj.IntField = 42;
        obj.UIntField = (uint)obj.IntField;
        TestTracker.Record("field.chain.IntToUInt", obj.UIntField == 42u);

        obj.ByteField = (byte)(obj.IntField % 256);
        TestTracker.Record("field.chain.IntToByte", obj.ByteField == 42);
    }

    private static void TestStaticFieldSequence()
    {
        // Write sequence of static fields
        s_staticSByte = -10;
        s_staticByte = 200;
        s_staticShort = -1000;
        s_staticUShort = 50000;
        s_staticInt = 123456;
        s_staticUInt = 3000000000u;
        s_staticLong = 0x123456789ABCDEF0L;
        s_staticULong = 0xFEDCBA9876543210UL;

        // Verify all fields
        TestTracker.Record("static.seq.SByte", s_staticSByte == -10);
        TestTracker.Record("static.seq.Byte", s_staticByte == 200);
        TestTracker.Record("static.seq.Short", s_staticShort == -1000);
        TestTracker.Record("static.seq.UShort", s_staticUShort == 50000);
        TestTracker.Record("static.seq.Int", s_staticInt == 123456);
        TestTracker.Record("static.seq.UInt", s_staticUInt == 3000000000u);
        TestTracker.Record("static.seq.Long", s_staticLong == 0x123456789ABCDEF0L);
        TestTracker.Record("static.seq.ULong", s_staticULong == 0xFEDCBA9876543210UL);

        // Static field interactions
        s_staticInt = 100;
        s_staticLong = s_staticInt * 10L;
        TestTracker.Record("static.seq.Interact", s_staticLong == 1000L);
    }

    private static void TestFieldWithExpressions()
    {
        var obj = new AllFieldsClass();

        // Ternary expressions with fields
        obj.IntField = 50;
        obj.LongField = obj.IntField > 25 ? 100L : 0L;
        TestTracker.Record("field.expr.Ternary", obj.LongField == 100L);

        // Comparison with field
        obj.IntField = 42;
        bool result = obj.IntField == 42;
        TestTracker.Record("field.expr.Compare", result);

        // Field in boolean expression
        obj.BoolField = obj.IntField > 40 && obj.IntField < 50;
        TestTracker.Record("field.expr.BoolExpr", obj.BoolField);

        // Arithmetic expression using multiple fields
        obj.IntField = 10;
        obj.LongField = 20L;
        long sum = obj.IntField + obj.LongField;
        TestTracker.Record("field.expr.MultiField", sum == 30L);

        // Field as array index
        obj.IntField = 2;
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        TestTracker.Record("field.expr.ArrayIndex", arr[obj.IntField] == 30);
    }

    private static void TestNestedFieldAccess()
    {
        var obj = new AllFieldsClass();

        // Access nested struct fields
        obj.SmallStructField = new SmallStruct { X = 10, Y = 20 };
        int sum = obj.SmallStructField.X + obj.SmallStructField.Y;
        TestTracker.Record("field.nested.Sum", sum == 30);

        // Modify nested struct field
        var temp = obj.SmallStructField;
        temp.X = 100;
        obj.SmallStructField = temp;
        TestTracker.Record("field.nested.Modify", obj.SmallStructField.X == 100);

        // Access large nested struct
        obj.LargeStructField = new LargeStruct { A = 1, B = 2, C = 3, D = 4 };
        long total = obj.LargeStructField.A + obj.LargeStructField.B +
                     obj.LargeStructField.C + obj.LargeStructField.D;
        TestTracker.Record("field.nested.LargeSum", total == 10L);

        // Double nested
        obj.NestedStructField = new NestedStruct
        {
            Inner = new SmallStruct { X = 5, Y = 6 },
            Z = 7
        };
        int nestedSum = obj.NestedStructField.Inner.X + obj.NestedStructField.Inner.Y + obj.NestedStructField.Z;
        TestTracker.Record("field.nested.DoubleNested", nestedSum == 18);
    }

    #endregion
}
