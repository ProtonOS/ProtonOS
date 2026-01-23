// JITTest - Delegate Tests
// Tests delegate creation and invocation

using ProtonOS.DDK.Kernel;

namespace JITTest;

// Delegate types
public delegate int DelegateIntFunc(int x);
public delegate int DelegateIntIntFunc(int x, int y);
public delegate void DelegateVoidAction();

/// <summary>
/// Delegate tests - verifies delegate creation and invocation.
/// Tests static delegates, instance delegates, and virtual delegates.
/// </summary>
public static class DelegateTests
{
    private static int _sideEffect;

    public static void RunAll()
    {
        TestStaticDelegate();
        TestStaticDelegateTwoArgs();
        TestVoidDelegate();
        TestDelegateInvoke();
        TestDelegateReassign();
        TestInstanceDelegate();
        TestVirtualDelegate();
    }

    private static void TestStaticDelegate()
    {
        DelegateIntFunc f = Double;
        int result = f(21);
        TestTracker.Record("delegate.Static", result == 42);
    }

    private static void TestStaticDelegateTwoArgs()
    {
        DelegateIntIntFunc f = Add;
        int result = f(20, 22);
        TestTracker.Record("delegate.StaticTwoArgs", result == 42);
    }

    private static void TestVoidDelegate()
    {
        _sideEffect = 0;
        DelegateVoidAction a = SetSideEffect;
        a();
        TestTracker.Record("delegate.Void", _sideEffect == 42);
    }

    private static void TestDelegateInvoke()
    {
        DelegateIntFunc f = Double;
        int result = f.Invoke(21);
        TestTracker.Record("delegate.Invoke", result == 42);
    }

    private static void TestDelegateReassign()
    {
        DelegateIntFunc f = Double;
        int first = f(10);  // 20
        f = Triple;
        int second = f(10);  // 30
        TestTracker.Record("delegate.Reassign", first + second == 50);
    }

    private static void TestInstanceDelegate()
    {
        DelegateTestClass obj = new DelegateTestClass();
        obj.InstanceValue = 3;
        DelegateIntFunc f = obj.InstanceDouble;
        int result = f(14);  // 14 * 3 = 42
        TestTracker.Record("delegate.Instance", result == 42);
    }

    private static void TestVirtualDelegate()
    {
        DelegateVirtualBase obj = new DelegateVirtualDerived();
        DelegateIntFunc f = obj.GetValue;
        int result = f(21);  // Derived returns x*2=42
        TestTracker.Record("delegate.Virtual", result == 42);
    }

    // Helper methods
    public static int Double(int x) => x * 2;
    public static int Triple(int x) => x * 3;
    public static int Add(int x, int y) => x + y;
    public static void SetSideEffect() { _sideEffect = 42; }
}

// Supporting types
public class DelegateTestClass
{
    public int InstanceValue = 2;
    public int InstanceDouble(int x) => x * InstanceValue;
}

public class DelegateVirtualBase
{
    public virtual int GetValue(int x) => x * 1;
}

public class DelegateVirtualDerived : DelegateVirtualBase
{
    public override int GetValue(int x) => x * 2;
}
