// JITTest - Object Model Tests
// Sections 15-16: callvirt, newobj, castclass, isinst, box, unbox, etc.

namespace JITTest;

/// <summary>
/// Tests for object model instructions
/// </summary>
public static class ObjectModelTests
{
    public static void RunAll()
    {
        TestCallvirtBasic();
        TestCallvirtInterface();
        TestCallvirtInheritance();
        TestCallvirtReturnTypes();
        TestCallvirtParameters();
        TestNewobj();
        TestNewobjParameters();
        TestNewobjStruct();
        TestCastclass();
        TestCastclassHierarchy();
        TestIsinst();
        TestIsinstHierarchy();
        TestBox();
        TestBoxAllTypes();
        TestUnbox();
        TestUnboxAllTypes();
    }

    #region Helper Types

    private interface ITestInterface { int InterfaceMethod(); }
    private interface ISecondInterface { int SecondMethod(); }
    private interface IGenericInterface<T> { T GetValue(); }

    private class BaseClass
    {
        public virtual int VirtualMethod() => 0;
        public virtual int VirtualWithArg(int x) => x;
        public virtual string VirtualReturnsString() => "base";
        public virtual long VirtualReturnsLong() => 0L;
        public int NonVirtualMethod() => -1;
    }

    private class DerivedClass : BaseClass, ITestInterface, ISecondInterface
    {
        public override int VirtualMethod() => 42;
        public override int VirtualWithArg(int x) => x * 2;
        public override string VirtualReturnsString() => "derived";
        public override long VirtualReturnsLong() => 0x123456789ABCDEF0L;
        public int InterfaceMethod() => 100;
        public int SecondMethod() => 200;
        public int DerivedOnlyMethod() => 300;
    }

    private class DeepDerivedClass : DerivedClass
    {
        public override int VirtualMethod() => 99;
        public override string VirtualReturnsString() => "deep";
    }

    private sealed class SealedDerivedClass : BaseClass
    {
        public override int VirtualMethod() => 77;
    }

    private class SimpleClass { public int Value; }

    private class ClassWithArgs
    {
        public int A;
        public int B;
        public string? S;

        public ClassWithArgs(int a) { A = a; }
        public ClassWithArgs(int a, int b) { A = a; B = b; }
        public ClassWithArgs(int a, int b, string s) { A = a; B = b; S = s; }
    }

    private struct SimpleStruct
    {
        public int Value;
        public SimpleStruct(int v) => Value = v;
    }

    private struct LargeStruct
    {
        public long A;
        public long B;
        public long C;
        public LargeStruct(long a, long b, long c) { A = a; B = b; C = c; }
    }

    private struct StructWithRef
    {
        public int Value;
        public string? Text;
        public StructWithRef(int v, string t) { Value = v; Text = t; }
    }

    #endregion

    #region callvirt (0x6F) - Basic Virtual Dispatch

    private static void TestCallvirtBasic()
    {
        var obj = new DerivedClass();

        // Call virtual method
        TestTracker.Record("callvirt.Virtual", obj.VirtualMethod() == 42);

        // Call through base reference - polymorphic dispatch
        BaseClass baseRef = obj;
        TestTracker.Record("callvirt.Polymorphic", baseRef.VirtualMethod() == 42);

        // Call non-overridden virtual (uses base implementation)
        var baseObj = new BaseClass();
        TestTracker.Record("callvirt.BaseImpl", baseObj.VirtualMethod() == 0);

        // Additional basic tests
        TestTracker.Record("callvirt.VirtualRet42", obj.VirtualMethod() == 42);
        TestTracker.Record("callvirt.BaseRet0", baseObj.VirtualMethod() == 0);
        TestTracker.Record("callvirt.PolyRet42", ((BaseClass)obj).VirtualMethod() == 42);

        // Sealed class virtual dispatch - tests polymorphism through base reference
        // This works because the JIT only devirtualizes for AOT types with optimized vtables,
        // not for JIT types which have full vtables supporting proper virtual dispatch.
        var sealedObj = new SealedDerivedClass();
        TestTracker.Record("callvirt.Sealed", sealedObj.VirtualMethod() == 77);
        TestTracker.Record("callvirt.SealedThroughBase", ((BaseClass)sealedObj).VirtualMethod() == 77);

        // Deep inheritance chain
        var deepObj = new DeepDerivedClass();
        TestTracker.Record("callvirt.DeepInherit", deepObj.VirtualMethod() == 99);
        TestTracker.Record("callvirt.DeepThroughBase", ((BaseClass)deepObj).VirtualMethod() == 99);
    }

    #endregion

    #region callvirt - Interface Dispatch

    private static void TestCallvirtInterface()
    {
        var obj = new DerivedClass();

        // Call interface method
        TestTracker.Record("callvirt.Interface", ((ITestInterface)obj).InterfaceMethod() == 100);

        // Multiple interfaces
        TestTracker.Record("callvirt.SecondInterface", ((ISecondInterface)obj).SecondMethod() == 200);

        // Interface variable
        ITestInterface iface = obj;
        TestTracker.Record("callvirt.InterfaceVar", iface.InterfaceMethod() == 100);

        // Deep derived through interface
        ITestInterface deepIface = new DeepDerivedClass();
        TestTracker.Record("callvirt.DeepInterface", deepIface.InterfaceMethod() == 100);
    }

    #endregion

    #region callvirt - Inheritance Scenarios

    private static void TestCallvirtInheritance()
    {
        // Method hidden by 'new' keyword - not applicable here, just test override chain
        var derived = new DerivedClass();
        var deep = new DeepDerivedClass();

        // Ensure override chain works
        TestTracker.Record("callvirt.OverrideChain1", derived.VirtualMethod() == 42);
        TestTracker.Record("callvirt.OverrideChain2", deep.VirtualMethod() == 99);

        // Non-virtual through derived
        TestTracker.Record("callvirt.NonVirtual", derived.NonVirtualMethod() == -1);

        // Derived-only method
        TestTracker.Record("callvirt.DerivedOnly", derived.DerivedOnlyMethod() == 300);
    }

    #endregion

    #region callvirt - Return Types

    private static void TestCallvirtReturnTypes()
    {
        var obj = new DerivedClass();
        BaseClass baseRef = obj;

        // int32 return
        TestTracker.Record("callvirt.ReturnInt32", baseRef.VirtualMethod() == 42);

        // int64 return
        TestTracker.Record("callvirt.ReturnInt64", baseRef.VirtualReturnsLong() == 0x123456789ABCDEF0L);

        // String return
        TestTracker.Record("callvirt.ReturnString", baseRef.VirtualReturnsString() == "derived");

        // Deep derived string return
        BaseClass deepRef = new DeepDerivedClass();
        TestTracker.Record("callvirt.DeepReturnString", deepRef.VirtualReturnsString() == "deep");
    }

    #endregion

    #region callvirt - Parameters

    private static void TestCallvirtParameters()
    {
        var obj = new DerivedClass();
        BaseClass baseRef = obj;

        // Single parameter
        TestTracker.Record("callvirt.SingleArg", baseRef.VirtualWithArg(10) == 20);

        // Different values
        TestTracker.Record("callvirt.ArgZero", baseRef.VirtualWithArg(0) == 0);
        TestTracker.Record("callvirt.ArgNegative", baseRef.VirtualWithArg(-5) == -10);
        TestTracker.Record("callvirt.ArgLarge", baseRef.VirtualWithArg(1000000) == 2000000);
    }

    #endregion

    #region newobj (0x73) - Basic

    private static void TestNewobj()
    {
        // Default constructor
        var obj1 = new SimpleClass();
        TestTracker.Record("newobj.DefaultCtor", obj1 != null);

        // Class with default field values
        TestTracker.Record("newobj.FieldDefault", obj1.Value == 0);

        // Set field after construction
        obj1.Value = 42;
        TestTracker.Record("newobj.SetField", obj1.Value == 42);

        // Multiple allocations
        var obj2 = new SimpleClass();
        TestTracker.Record("newobj.MultipleAlloc", obj1 != obj2);
    }

    #endregion

    #region newobj - With Parameters

    private static void TestNewobjParameters()
    {
        // Single parameter
        var obj1 = new ClassWithArgs(42);
        TestTracker.Record("newobj.OneArg", obj1.A == 42);

        // Two parameters
        var obj2 = new ClassWithArgs(10, 20);
        TestTracker.Record("newobj.TwoArgs", obj2.A == 10 && obj2.B == 20);

        // Three parameters including string
        var obj3 = new ClassWithArgs(1, 2, "test");
        TestTracker.Record("newobj.ThreeArgs", obj3.A == 1 && obj3.B == 2 && obj3.S == "test");

        // Negative values
        var obj4 = new ClassWithArgs(-100);
        TestTracker.Record("newobj.NegativeArg", obj4.A == -100);
    }

    #endregion

    #region newobj - Structs

    private static void TestNewobjStruct()
    {
        // Simple struct
        var s1 = new SimpleStruct(10);
        TestTracker.Record("newobj.SimpleStruct", s1.Value == 10);

        // Large struct
        var s2 = new LargeStruct(1, 2, 3);
        TestTracker.Record("newobj.LargeStruct", s2.A == 1 && s2.B == 2 && s2.C == 3);

        // Struct with reference field
        var s3 = new StructWithRef(42, "hello");
        TestTracker.Record("newobj.StructWithRef", s3.Value == 42 && s3.Text == "hello");

        // Default struct (no newobj, uses initobj)
        SimpleStruct s4 = default;
        TestTracker.Record("newobj.DefaultStruct", s4.Value == 0);
    }

    #endregion

    #region castclass (0x74) - Basic

    private static void TestCastclass()
    {
        // Valid string cast
        object obj = "test";
        TestTracker.Record("castclass.String", (string)obj == "test");

        // Null cast - returns null
        object? nullObj = null;
        TestTracker.Record("castclass.Null", (string?)nullObj == null);

        // Same type cast
        string str = "hello";
        object objStr = str;
        TestTracker.Record("castclass.SameType", (string)objStr == "hello");
    }

    #endregion

    #region castclass - Hierarchy

    private static void TestCastclassHierarchy()
    {
        var derived = new DerivedClass();

        // Derived to base
        object obj = derived;
        TestTracker.Record("castclass.ToBase", ((BaseClass)obj).VirtualMethod() == 42);

        // Cast to interface
        TestTracker.Record("castclass.ToInterface", ((ITestInterface)obj).InterfaceMethod() == 100);

        // Deep hierarchy
        var deep = new DeepDerivedClass();
        object deepObj = deep;
        TestTracker.Record("castclass.DeepToBase", ((BaseClass)deepObj).VirtualMethod() == 99);
        TestTracker.Record("castclass.DeepToDerived", ((DerivedClass)deepObj).DerivedOnlyMethod() == 300);

        // Array cast
        int[] arr = new int[] { 1, 2, 3 };
        object arrObj = arr;
        TestTracker.Record("castclass.Array", ((int[])arrObj).Length == 3);
    }

    #endregion

    #region isinst (0x75) - Basic

    private static void TestIsinst()
    {
        object obj = "test";

        // Matching type
        TestTracker.Record("isinst.Match", obj is string);

        // Non-matching type
        TestTracker.Record("isinst.NoMatch", !(obj is int));

        // Null - isinst always returns null for null
        object? nullObj = null;
        TestTracker.Record("isinst.NullInput", !(nullObj is string));

        // Pattern matching extraction
        if (obj is string s)
            TestTracker.Record("isinst.PatternMatch", s == "test");
        else
            TestTracker.Record("isinst.PatternMatch", false);
    }

    #endregion

    #region isinst - Hierarchy

    private static void TestIsinstHierarchy()
    {
        var derived = new DerivedClass();
        object obj = derived;

        // Is base type
        TestTracker.Record("isinst.IsBase", obj is BaseClass);

        // Is interface
        TestTracker.Record("isinst.IsInterface", obj is ITestInterface);

        // Is second interface
        TestTracker.Record("isinst.IsSecondInterface", obj is ISecondInterface);

        // Is not unrelated type
        TestTracker.Record("isinst.NotUnrelated", !(obj is string));

        // Deep hierarchy
        var deep = new DeepDerivedClass();
        object deepObj = deep;
        TestTracker.Record("isinst.DeepIsBase", deepObj is BaseClass);
        TestTracker.Record("isinst.DeepIsDerived", deepObj is DerivedClass);
        TestTracker.Record("isinst.DeepIsDeep", deepObj is DeepDerivedClass);

        // Array type check
        int[] arr = new int[] { 1, 2, 3 };
        object arrObj = arr;
        TestTracker.Record("isinst.ArrayType", arrObj is int[]);
        TestTracker.Record("isinst.NotWrongArray", !(arrObj is string[]));
    }

    #endregion

    #region box (0x8C) - Basic

    private static void TestBox()
    {
        // Int32
        int i = 42;
        object boxed = i;
        TestTracker.Record("box.Int32", (int)boxed == 42);

        // Int64
        long l = 0x123456789ABCDEF0L;
        object boxedL = l;
        TestTracker.Record("box.Int64", (long)boxedL == 0x123456789ABCDEF0L);

        // Struct
        var s = new SimpleStruct(10);
        object boxedS = s;
        TestTracker.Record("box.Struct", ((SimpleStruct)boxedS).Value == 10);

        // Boxing creates a copy
        s.Value = 20;
        TestTracker.Record("box.IsCopy", ((SimpleStruct)boxedS).Value == 10);

        // Large struct
        var large = new LargeStruct(1, 2, 3);
        object boxedLarge = large;
        TestTracker.Record("box.LargeStruct", ((LargeStruct)boxedLarge).A == 1);

        // Additional box tests
        int zero = 0;
        object boxedZero = zero;
        TestTracker.Record("box.Int32.Zero", (int)boxedZero == 0);

        int negOne = -1;
        object boxedNegOne = negOne;
        TestTracker.Record("box.Int32.NegOne", (int)boxedNegOne == -1);

        int max = int.MaxValue;
        object boxedMax = max;
        TestTracker.Record("box.Int32.Max", (int)boxedMax == int.MaxValue);

        int min = int.MinValue;
        object boxedMin = min;
        TestTracker.Record("box.Int32.Min", (int)boxedMin == int.MinValue);

        long lZero = 0L;
        object boxedLZero = lZero;
        TestTracker.Record("box.Int64.Zero", (long)boxedLZero == 0L);

        long lNegOne = -1L;
        object boxedLNegOne = lNegOne;
        TestTracker.Record("box.Int64.NegOne", (long)boxedLNegOne == -1L);
    }

    #endregion

    #region box - All Primitive Types

    private static void TestBoxAllTypes()
    {
        // sbyte - positive value
        sbyte sb = 42;
        object boxedSb = sb;
        TestTracker.Record("box.SByte", (sbyte)boxedSb == 42);

        // sbyte - negative value (sign extension issue under investigation)
        sbyte sbNeg = -42;
        object boxedSbNeg = sbNeg;
        TestTracker.Record("box.SByteNeg", (sbyte)boxedSbNeg == -42);

        // byte
        byte b = 200;
        object boxedB = b;
        TestTracker.Record("box.Byte", (byte)boxedB == 200);

        // short - positive value
        short sh = 1000;
        object boxedSh = sh;
        TestTracker.Record("box.Short", (short)boxedSh == 1000);

        // short - negative value (sign extension issue under investigation)
        short shNeg = -1000;
        object boxedShNeg = shNeg;
        TestTracker.Record("box.ShortNeg", (short)boxedShNeg == -1000);

        // ushort
        ushort us = 50000;
        object boxedUs = us;
        TestTracker.Record("box.UShort", (ushort)boxedUs == 50000);

        // uint
        uint ui = 42u;
        object boxedUi = ui;
        TestTracker.Record("box.UInt", (uint)boxedUi == 42u);

        // ulong
        ulong ul = 0xFEDCBA9876543210UL;
        object boxedUl = ul;
        TestTracker.Record("box.ULong", (ulong)boxedUl == 0xFEDCBA9876543210UL);

        // float
        float f = 3.14f;
        object boxedF = f;
        TestTracker.Record("box.Float", (float)boxedF == 3.14f);

        // double
        double d = 2.71828;
        object boxedD = d;
        TestTracker.Record("box.Double", (double)boxedD == 2.71828);

        // bool
        bool bl = true;
        object boxedBl = bl;
        TestTracker.Record("box.Bool", (bool)boxedBl == true);

        // char
        char c = 'X';
        object boxedC = c;
        TestTracker.Record("box.Char", (char)boxedC == 'X');

        // Additional primitive box tests
        byte bZero = 0;
        object boxedBZero = bZero;
        TestTracker.Record("box.Byte.Zero", (byte)boxedBZero == 0);

        byte bMax = 255;
        object boxedBMax = bMax;
        TestTracker.Record("box.Byte.Max", (byte)boxedBMax == 255);

        sbyte sbZero = 0;
        object boxedSbZero = sbZero;
        TestTracker.Record("box.SByte.Zero", (sbyte)boxedSbZero == 0);

        sbyte sbMax = 127;
        object boxedSbMax = sbMax;
        TestTracker.Record("box.SByte.Max", (sbyte)boxedSbMax == 127);

        short shZero = 0;
        object boxedShZero = shZero;
        TestTracker.Record("box.Short.Zero", (short)boxedShZero == 0);

        short shMax = short.MaxValue;
        object boxedShMax = shMax;
        TestTracker.Record("box.Short.Max", (short)boxedShMax == short.MaxValue);

        ushort usZero = 0;
        object boxedUsZero = usZero;
        TestTracker.Record("box.UShort.Zero", (ushort)boxedUsZero == 0);

        ushort usMax = ushort.MaxValue;
        object boxedUsMax = usMax;
        TestTracker.Record("box.UShort.Max", (ushort)boxedUsMax == ushort.MaxValue);

        uint uiZero = 0u;
        object boxedUiZero = uiZero;
        TestTracker.Record("box.UInt.Zero", (uint)boxedUiZero == 0u);

        uint uiLarge = 0x7FFFFFFFu;  // Max positive int32 as uint
        object boxedUiLarge = uiLarge;
        TestTracker.Record("box.UInt.Large", (uint)boxedUiLarge == 0x7FFFFFFFu);

        uint uiMax = uint.MaxValue;  // 0xFFFFFFFF - tests sign extension
        object boxedUiMax = uiMax;
        TestTracker.Record("box.UInt.Max", (uint)boxedUiMax == uint.MaxValue);

        ulong ulZero = 0UL;
        object boxedUlZero = ulZero;
        TestTracker.Record("box.ULong.Zero", (ulong)boxedUlZero == 0UL);

        ulong ulMax = ulong.MaxValue;
        object boxedUlMax = ulMax;
        TestTracker.Record("box.ULong.Max", (ulong)boxedUlMax == ulong.MaxValue);

        float fZero = 0.0f;
        object boxedFZero = fZero;
        TestTracker.Record("box.Float.Zero", (float)boxedFZero == 0.0f);

        float fNeg = -1.5f;
        object boxedFNeg = fNeg;
        TestTracker.Record("box.Float.Neg", (float)boxedFNeg == -1.5f);

        double dZero = 0.0;
        object boxedDZero = dZero;
        TestTracker.Record("box.Double.Zero", (double)boxedDZero == 0.0);

        double dNeg = -1.5;
        object boxedDNeg = dNeg;
        TestTracker.Record("box.Double.Neg", (double)boxedDNeg == -1.5);

        bool bFalse = false;
        object boxedFalse = bFalse;
        TestTracker.Record("box.Bool.False", (bool)boxedFalse == false);

        char cA = 'A';
        object boxedCA = cA;
        TestTracker.Record("box.Char.A", (char)boxedCA == 'A');

        char cNull = '\0';
        object boxedCNull = cNull;
        TestTracker.Record("box.Char.Null", (char)boxedCNull == '\0');
    }

    #endregion

    #region unbox / unbox.any (0x79, 0xA5) - Basic

    private static void TestUnbox()
    {
        // Int32
        object boxed = 42;
        TestTracker.Record("unbox.Int32", (int)boxed == 42);

        // Int64
        object boxedLong = 0x123456789ABCDEF0L;
        TestTracker.Record("unbox.Int64", (long)boxedLong == 0x123456789ABCDEF0L);

        // Struct
        object boxedStruct = new SimpleStruct(10);
        TestTracker.Record("unbox.Struct", ((SimpleStruct)boxedStruct).Value == 10);

        // Large struct
        object boxedLarge = new LargeStruct(1, 2, 3);
        var unboxedLarge = (LargeStruct)boxedLarge;
        TestTracker.Record("unbox.LargeStruct", unboxedLarge.A == 1 && unboxedLarge.C == 3);
    }

    #endregion

    #region unbox - All Primitive Types

    private static void TestUnboxAllTypes()
    {
        // sbyte - use positive value
        object boxedSb = (sbyte)42;
        TestTracker.Record("unbox.SByte", (sbyte)boxedSb == 42);

        // byte
        object boxedB = (byte)200;
        TestTracker.Record("unbox.Byte", (byte)boxedB == 200);

        // short - use positive value
        object boxedSh = (short)1000;
        TestTracker.Record("unbox.Short", (short)boxedSh == 1000);

        // ushort
        object boxedUs = (ushort)50000;
        TestTracker.Record("unbox.UShort", (ushort)boxedUs == 50000);

        // uint
        object boxedUi = 42u;
        TestTracker.Record("unbox.UInt", (uint)boxedUi == 42u);

        // ulong
        object boxedUl = 0xFEDCBA9876543210UL;
        TestTracker.Record("unbox.ULong", (ulong)boxedUl == 0xFEDCBA9876543210UL);

        // float
        object boxedF = 3.14f;
        TestTracker.Record("unbox.Float", (float)boxedF == 3.14f);

        // double
        object boxedD = 2.71828;
        TestTracker.Record("unbox.Double", (double)boxedD == 2.71828);

        // bool
        object boxedBl = true;
        TestTracker.Record("unbox.Bool", (bool)boxedBl == true);

        // char
        object boxedC = 'X';
        TestTracker.Record("unbox.Char", (char)boxedC == 'X');

        // Struct with ref
        object boxedSwr = new StructWithRef(42, "hello");
        var unboxedSwr = (StructWithRef)boxedSwr;
        TestTracker.Record("unbox.StructWithRef", unboxedSwr.Value == 42 && unboxedSwr.Text == "hello");

        // Additional unbox tests for edge values
        object boxedIntZero = 0;
        TestTracker.Record("unbox.Int32.Zero", (int)boxedIntZero == 0);

        object boxedIntMax = int.MaxValue;
        TestTracker.Record("unbox.Int32.Max", (int)boxedIntMax == int.MaxValue);

        object boxedIntMin = int.MinValue;
        TestTracker.Record("unbox.Int32.Min", (int)boxedIntMin == int.MinValue);

        object boxedLongZero = 0L;
        TestTracker.Record("unbox.Int64.Zero", (long)boxedLongZero == 0L);

        object boxedLongMax = long.MaxValue;
        TestTracker.Record("unbox.Int64.Max", (long)boxedLongMax == long.MaxValue);

        object boxedLongMin = long.MinValue;
        TestTracker.Record("unbox.Int64.Min", (long)boxedLongMin == long.MinValue);

        object boxedByteZero = (byte)0;
        TestTracker.Record("unbox.Byte.Zero", (byte)boxedByteZero == 0);

        object boxedByteMax = (byte)255;
        TestTracker.Record("unbox.Byte.Max", (byte)boxedByteMax == 255);

        object boxedUIntZero = 0u;
        TestTracker.Record("unbox.UInt.Zero", (uint)boxedUIntZero == 0u);

        object boxedUIntLarge = 0x7FFFFFFFu;  // Max positive int32 as uint
        TestTracker.Record("unbox.UInt.Large", (uint)boxedUIntLarge == 0x7FFFFFFFu);

        object boxedUIntMax = uint.MaxValue;  // 0xFFFFFFFF - tests sign extension
        TestTracker.Record("unbox.UInt.Max", (uint)boxedUIntMax == uint.MaxValue);

        object boxedULongZero = 0UL;
        TestTracker.Record("unbox.ULong.Zero", (ulong)boxedULongZero == 0UL);

        object boxedULongMax = ulong.MaxValue;
        TestTracker.Record("unbox.ULong.Max", (ulong)boxedULongMax == ulong.MaxValue);

        object boxedFloatZero = 0.0f;
        TestTracker.Record("unbox.Float.Zero", (float)boxedFloatZero == 0.0f);

        object boxedFloatNeg = -1.5f;
        TestTracker.Record("unbox.Float.Neg", (float)boxedFloatNeg == -1.5f);

        object boxedDoubleZero = 0.0;
        TestTracker.Record("unbox.Double.Zero", (double)boxedDoubleZero == 0.0);

        object boxedDoubleNeg = -1.5;
        TestTracker.Record("unbox.Double.Neg", (double)boxedDoubleNeg == -1.5);

        object boxedBoolFalse = false;
        TestTracker.Record("unbox.Bool.False", (bool)boxedBoolFalse == false);

        object boxedCharA = 'A';
        TestTracker.Record("unbox.Char.A", (char)boxedCharA == 'A');

        object boxedCharNull = '\0';
        TestTracker.Record("unbox.Char.Null", (char)boxedCharNull == '\0');
    }

    #endregion
}
