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
        TestCallvirt();
        TestNewobj();
        TestCastclass();
        TestIsinst();
        TestBox();
        TestUnbox();
    }

    #region callvirt (0x6F)

    private static void TestCallvirt()
    {
        var obj = new DerivedClass();
        TestTracker.Record("callvirt.Virtual", obj.VirtualMethod() == 42);
        TestTracker.Record("callvirt.Override", ((BaseClass)obj).VirtualMethod() == 42);
        TestTracker.Record("callvirt.Interface", ((ITestInterface)obj).InterfaceMethod() == 100);
    }

    private interface ITestInterface { int InterfaceMethod(); }
    private class BaseClass { public virtual int VirtualMethod() => 0; }
    private class DerivedClass : BaseClass, ITestInterface
    {
        public override int VirtualMethod() => 42;
        public int InterfaceMethod() => 100;
    }

    #endregion

    #region newobj (0x73)

    private static void TestNewobj()
    {
        var obj1 = new SimpleClass();
        TestTracker.Record("newobj.DefaultCtor", obj1 != null);

        var obj2 = new ClassWithArgs(42);
        TestTracker.Record("newobj.WithArgs", obj2.Value == 42);

        var s = new SimpleStruct(10);
        TestTracker.Record("newobj.Struct", s.Value == 10);
    }

    private class SimpleClass { }
    private class ClassWithArgs { public int Value; public ClassWithArgs(int v) => Value = v; }
    private struct SimpleStruct { public int Value; public SimpleStruct(int v) => Value = v; }

    #endregion

    #region castclass (0x74)

    private static void TestCastclass()
    {
        object obj = "test";
        TestTracker.Record("castclass.Valid", (string)obj == "test");

        object derived = new DerivedClass();
        TestTracker.Record("castclass.ToBase", ((BaseClass)derived).VirtualMethod() == 42);

        object? nullObj = null;
        TestTracker.Record("castclass.Null", (string?)nullObj == null);
    }

    #endregion

    #region isinst (0x75)

    private static void TestIsinst()
    {
        object obj = "test";
        TestTracker.Record("isinst.Match", obj is string);
        TestTracker.Record("isinst.NoMatch", !(obj is int));

        object? nullObj = null;
        TestTracker.Record("isinst.Null", nullObj is not string);

        object derived = new DerivedClass();
        TestTracker.Record("isinst.Base", derived is BaseClass);
        TestTracker.Record("isinst.Interface", derived is ITestInterface);
    }

    #endregion

    #region box (0x8C)

    private static void TestBox()
    {
        int i = 42;
        object boxed = i;
        TestTracker.Record("box.Int32", (int)boxed == 42);

        long l = 100L;
        object boxedL = l;
        TestTracker.Record("box.Int64", (long)boxedL == 100L);

        var s = new SimpleStruct(10);
        object boxedS = s;
        TestTracker.Record("box.Struct", ((SimpleStruct)boxedS).Value == 10);

        // Boxing creates a copy
        s.Value = 20;
        TestTracker.Record("box.IsCopy", ((SimpleStruct)boxedS).Value == 10);
    }

    #endregion

    #region unbox / unbox.any (0x79, 0xA5)

    private static void TestUnbox()
    {
        object boxed = 42;
        TestTracker.Record("unbox.any.Int32", (int)boxed == 42);

        object boxedLong = 100L;
        TestTracker.Record("unbox.any.Int64", (long)boxedLong == 100L);

        object boxedStruct = new SimpleStruct(10);
        TestTracker.Record("unbox.any.Struct", ((SimpleStruct)boxedStruct).Value == 10);
    }

    #endregion
}
