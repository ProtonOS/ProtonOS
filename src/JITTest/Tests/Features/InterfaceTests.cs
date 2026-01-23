// JITTest - Interface Tests
// Tests interface dispatch, isinst, castclass, and default interface methods

using ProtonOS.DDK.Kernel;

namespace JITTest;

/// <summary>
/// Interface tests - verifies interface method dispatch via callvirt.
/// Tests isinst, castclass, explicit implementations, default interface methods.
/// </summary>
public static class InterfaceTests
{
    public static void RunAll()
    {
        TestSimpleInterface();
        TestMultipleInterfacesFirst();
        TestMultipleInterfacesSecond();
        TestMultipleInterfacesThird();
        TestIsinstInterfaceSuccess();
        TestIsinstInterfaceFailure();
        TestIsinstNull();
        TestIsinstMultipleFirst();
        TestIsinstMultipleSecond();
        TestCastclassInterfaceSuccess();
        TestExplicitInterfaceImplicit();
        TestExplicitInterfaceExplicit();
        TestExplicitInterfaceBoth();
        TestThreeArgInterfaceDispatch();
        TestDefaultMethodBase();
        TestDefaultMethodNotOverridden();
        TestDefaultMethodFixed();
        TestDefaultMethodOverridden();
        TestDefaultMethodPartialOverride();
        TestIReadOnlyListCount();
        TestIReadOnlyListIndexer();
    }

    private static void TestSimpleInterface()
    {
        IInterfaceValue v = new InterfaceValueImpl();
        TestTracker.Record("interface.Simple", v.GetValue() == 42);
    }

    private static void TestMultipleInterfacesFirst()
    {
        InterfaceMultiImpl obj = new InterfaceMultiImpl();
        IInterfaceValue v = obj;
        TestTracker.Record("interface.MultiFirst", v.GetValue() == 10);
    }

    private static void TestMultipleInterfacesSecond()
    {
        InterfaceMultiImpl obj = new InterfaceMultiImpl();
        IInterfaceMultiplier m = obj;
        TestTracker.Record("interface.MultiSecond", m.Multiply(4) == 40);
    }

    private static void TestMultipleInterfacesThird()
    {
        InterfaceMultiImpl obj = new InterfaceMultiImpl();
        IInterfaceAdder a = obj;
        TestTracker.Record("interface.MultiThird", a.Add(32) == 42);
    }

    private static void TestIsinstInterfaceSuccess()
    {
        object obj = new InterfaceValueImpl();
        IInterfaceValue v = obj as IInterfaceValue;
        TestTracker.Record("interface.IsinstSuccess", v != null);
    }

    private static void TestIsinstInterfaceFailure()
    {
        object obj = new InterfaceValueImpl();
        IInterfaceMultiplier m = obj as IInterfaceMultiplier;
        TestTracker.Record("interface.IsinstFailure", m == null);
    }

    private static void TestIsinstNull()
    {
        object obj = null;
        IInterfaceValue v = obj as IInterfaceValue;
        TestTracker.Record("interface.IsinstNull", v == null);
    }

    private static void TestIsinstMultipleFirst()
    {
        object obj = new InterfaceMultiImpl();
        IInterfaceValue v = obj as IInterfaceValue;
        TestTracker.Record("interface.IsinstMultiFirst", v != null && v.GetValue() == 10);
    }

    private static void TestIsinstMultipleSecond()
    {
        object obj = new InterfaceMultiImpl();
        IInterfaceMultiplier m = obj as IInterfaceMultiplier;
        TestTracker.Record("interface.IsinstMultiSecond", m != null && m.Multiply(4) == 40);
    }

    private static void TestCastclassInterfaceSuccess()
    {
        object obj = new InterfaceValueImpl();
        IInterfaceValue v = (IInterfaceValue)obj;
        TestTracker.Record("interface.Castclass", v.GetValue() == 42);
    }

    private static void TestExplicitInterfaceImplicit()
    {
        InterfaceExplicitImpl obj = new InterfaceExplicitImpl();
        IInterfaceValue v = obj;
        TestTracker.Record("interface.ExplicitImplicit", v.GetValue() == 10);
    }

    private static void TestExplicitInterfaceExplicit()
    {
        InterfaceExplicitImpl obj = new InterfaceExplicitImpl();
        IInterfaceExplicit e = obj;
        TestTracker.Record("interface.ExplicitExplicit", e.GetValue() == 42);
    }

    private static void TestExplicitInterfaceBoth()
    {
        InterfaceExplicitImpl obj = new InterfaceExplicitImpl();
        IInterfaceValue v = obj;
        IInterfaceExplicit e = obj;
        int implicitVal = v.GetValue();
        int explicitVal = e.GetValue();
        TestTracker.Record("interface.ExplicitBoth", implicitVal + explicitVal == 52);
    }

    private static void TestThreeArgInterfaceDispatch()
    {
        InterfaceTwoParamsImpl obj = new InterfaceTwoParamsImpl();
        int direct = obj.Compute(10, 32);
        IInterfaceTwoParams iface = obj;
        int ifaceResult = iface.Compute(10, 32);
        TestTracker.Record("interface.ThreeArgDirect", direct == 42);
        TestTracker.Record("interface.ThreeArgDispatch", ifaceResult == 42);
    }

    private static void TestDefaultMethodBase()
    {
        IInterfaceWithDefault obj = new InterfaceDefaultMethodImpl();
        TestTracker.Record("interface.DefaultBase", obj.GetBaseValue() == 21);
    }

    private static void TestDefaultMethodNotOverridden()
    {
        IInterfaceWithDefault obj = new InterfaceDefaultMethodImpl();
        TestTracker.Record("interface.DefaultNotOverridden", obj.GetDoubled() == 42);
    }

    private static void TestDefaultMethodFixed()
    {
        IInterfaceWithDefault obj = new InterfaceDefaultMethodImpl();
        TestTracker.Record("interface.DefaultFixed", obj.GetFixed() == 100);
    }

    private static void TestDefaultMethodOverridden()
    {
        IInterfaceWithDefault obj = new InterfacePartialOverrideImpl();
        TestTracker.Record("interface.DefaultOverridden", obj.GetDoubled() == 30);
    }

    private static void TestDefaultMethodPartialOverride()
    {
        IInterfaceWithDefault obj = new InterfacePartialOverrideImpl();
        TestTracker.Record("interface.DefaultPartialOverride", obj.GetFixed() == 100);
    }

    private static void TestIReadOnlyListCount()
    {
        var list = new System.Collections.Generic.List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        System.Collections.Generic.IReadOnlyList<int> readOnlyList = list;
        TestTracker.Record("interface.IReadOnlyListCount", readOnlyList.Count == 3);
    }

    private static void TestIReadOnlyListIndexer()
    {
        var list = new System.Collections.Generic.List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        System.Collections.Generic.IReadOnlyList<int> readOnlyList = list;
        int sum = readOnlyList[0] + readOnlyList[1] + readOnlyList[2];
        TestTracker.Record("interface.IReadOnlyListIndexer", sum == 60);
    }
}

// Supporting types for interface tests
public interface IInterfaceValue { int GetValue(); }
public interface IInterfaceMultiplier { int Multiply(int x); }
public interface IInterfaceAdder { int Add(int x); }
public interface IInterfaceExplicit { int GetValue(); }
public interface IInterfaceTwoParams { int Compute(int a, int b); }

public class InterfaceValueImpl : IInterfaceValue
{
    public int GetValue() => 42;
}

public class InterfaceMultiImpl : IInterfaceValue, IInterfaceMultiplier, IInterfaceAdder
{
    private int _value = 10;
    public int GetValue() => _value;
    public int Multiply(int x) => _value * x;
    public int Add(int x) => _value + x;
}

public class InterfaceExplicitImpl : IInterfaceValue, IInterfaceExplicit
{
    public int GetValue() => 10;  // Implicit IInterfaceValue implementation
    int IInterfaceExplicit.GetValue() => 42;  // Explicit IInterfaceExplicit implementation
}

public class InterfaceTwoParamsImpl : IInterfaceTwoParams
{
    public int Compute(int a, int b) => a + b;
}

// Default interface method types
public interface IInterfaceWithDefault
{
    int GetBaseValue();
    int GetDoubled() => GetBaseValue() * 2;
    int GetFixed() => 100;
}

public class InterfaceDefaultMethodImpl : IInterfaceWithDefault
{
    public int GetBaseValue() => 21;
}

public class InterfacePartialOverrideImpl : IInterfaceWithDefault
{
    public int GetBaseValue() => 10;
    public int GetDoubled() => GetBaseValue() * 3;  // Override to triple
}
