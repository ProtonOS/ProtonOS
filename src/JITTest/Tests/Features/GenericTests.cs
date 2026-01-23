// JITTest - Generic Tests
// Tests generic methods, classes, interfaces, and constraints

using ProtonOS.DDK.Kernel;

namespace JITTest;

/// <summary>
/// Generic tests - verifies generic type and method compilation.
/// Tests MethodSpec token resolution, MVAR/VAR instantiation.
/// </summary>
public static class GenericTests
{
    public static void RunAll()
    {
        TestGenericMethod();
        TestGenericClass();
        TestSimpleListBasic();
        TestSimpleListCount();
        TestMultiTypeParamConvert();
        TestMultiTypeParamCombine();
        TestGenericInterfaceInt();
        TestGenericInterfaceSetGet();
        TestGenericInterfaceString();
        TestGenericDelegate();
        TestGenericDelegateStringToInt();
        TestNestedGenericSimple();
        TestNestedGenericInnerValue();
        TestNestedGenericMethod();
        TestConstraintClass();
        TestConstraintClassNotNull();
        TestConstraintStruct();
        TestConstraintNew();
        TestConstraintInterface();
        TestConstraintBase();
        TestCovariance();
        TestContravariance();
    }

    private static void TestGenericMethod()
    {
        int result = Identity(42);
        TestTracker.Record("generic.Method", result == 42);
    }

    private static T Identity<T>(T value) => value;

    private static void TestGenericClass()
    {
        var box = new GenericBox<int>(42);
        TestTracker.Record("generic.Class", box.Value == 42);
    }

    private static void TestSimpleListBasic()
    {
        var list = new GenericSimpleList<int>();
        TestTracker.Record("generic.ListEmpty", list.Count == 0);
    }

    private static void TestSimpleListCount()
    {
        var list = new GenericSimpleList<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        TestTracker.Record("generic.ListCount", list.Count == 3);
    }

    private static void TestMultiTypeParamConvert()
    {
        int result = GenericMultiTypeHelper.Convert<string, int>("hello", 42);
        TestTracker.Record("generic.MultiTypeConvert", result == 42);
    }

    private static void TestMultiTypeParamCombine()
    {
        int result = GenericMultiTypeHelper.Combine<int, int>(0, 42);
        TestTracker.Record("generic.MultiTypeCombine", result == 42);
    }

    private static void TestGenericInterfaceInt()
    {
        IGenericContainer<int> container = new GenericContainer<int>(42);
        TestTracker.Record("generic.InterfaceInt", container.GetValue() == 42);
    }

    private static void TestGenericInterfaceSetGet()
    {
        IGenericContainer<int> container = new GenericContainer<int>(0);
        container.SetValue(42);
        TestTracker.Record("generic.InterfaceSetGet", container.GetValue() == 42);
    }

    private static void TestGenericInterfaceString()
    {
        IGenericContainer<string> container = new GenericContainer<string>("hello");
        TestTracker.Record("generic.InterfaceString", container.GetValue().Length == 5);
    }

    private static void TestGenericDelegate()
    {
        GenericTransformer<int, int> doubler = x => x * 2;
        TestTracker.Record("generic.Delegate", doubler(21) == 42);
    }

    private static void TestGenericDelegateStringToInt()
    {
        GenericTransformer<string, int> lengthGetter = s => s.Length;
        TestTracker.Record("generic.DelegateStringToInt", lengthGetter("hello") == 5);
    }

    private static void TestNestedGenericSimple()
    {
        var inner = new GenericOuter<int>.Inner<string>();
        inner.OuterValue = 42;
        inner.InnerValue = "hello";
        TestTracker.Record("generic.NestedSimple", inner.OuterValue == 42);
    }

    private static void TestNestedGenericInnerValue()
    {
        var inner = new GenericOuter<int>.Inner<string>();
        inner.InnerValue = "world";
        TestTracker.Record("generic.NestedInnerValue", inner.InnerValue.Length == 5);
    }

    private static void TestNestedGenericMethod()
    {
        var inner = new GenericOuter<int>.Inner<string>();
        inner.OuterValue = 20;
        inner.InnerValue = "ab";
        TestTracker.Record("generic.NestedMethod", inner.GetCombined() == 22);
    }

    private static void TestConstraintClass()
    {
        bool isNull = GenericConstrainedMethods.IsNull<string>(null);
        TestTracker.Record("generic.ConstraintClassNull", isNull);
    }

    private static void TestConstraintClassNotNull()
    {
        bool isNull = GenericConstrainedMethods.IsNull<string>("hello");
        TestTracker.Record("generic.ConstraintClassNotNull", !isNull);
    }

    private static void TestConstraintStruct()
    {
        int result = GenericConstrainedMethods.GetDefault<int>();
        TestTracker.Record("generic.ConstraintStruct", result == 0);
    }

    private static void TestConstraintNew()
    {
        GenericCreatable obj = GenericConstrainedMethods.CreateNew<GenericCreatable>();
        TestTracker.Record("generic.ConstraintNew", obj != null && obj.Value == 42);
    }

    private static void TestConstraintInterface()
    {
        GenericValueImpl impl = new GenericValueImpl();
        int result = GenericConstrainedMethods.GetFromInterface<GenericValueImpl>(impl);
        TestTracker.Record("generic.ConstraintInterface", result == 42);
    }

    private static void TestConstraintBase()
    {
        GenericDerivedWithValue derived = new GenericDerivedWithValue();
        int result = GenericConstrainedMethods.GetFromBase<GenericDerivedWithValue>(derived);
        TestTracker.Record("generic.ConstraintBase", result == 99);
    }

    private static void TestCovariance()
    {
        GenericDerivedWithValue d = new GenericDerivedWithValue();
        IGenericCovariant<GenericDerivedWithValue> derived = new GenericCovariantImpl<GenericDerivedWithValue>(d);
        IGenericCovariant<GenericBaseWithValue> baseRef = derived;
        TestTracker.Record("generic.Covariance", baseRef.Get().GetBaseValue() == 99);
    }

    private static void TestContravariance()
    {
        IGenericContravariant<GenericBaseWithValue> baseConsumer = new GenericContravariantImpl<GenericBaseWithValue>();
        IGenericContravariant<GenericDerivedWithValue> derivedConsumer = baseConsumer;
        GenericDerivedWithValue d = new GenericDerivedWithValue();
        derivedConsumer.Accept(d);
        TestTracker.Record("generic.Contravariance", true);
    }
}

// Supporting types for generic tests
public class GenericBox<T>
{
    public T Value { get; }
    public GenericBox(T value) { Value = value; }
}

public class GenericSimpleList<T>
{
    private T[] _items;
    private int _count;

    public GenericSimpleList()
    {
        _items = new T[8];
        _count = 0;
    }

    public int Count => _count;

    public void Add(T item)
    {
        if (_count < _items.Length)
            _items[_count] = item;
        _count++;
    }

    public T Get(int index) => _items[index];
}

public static class GenericMultiTypeHelper
{
    public static TResult Convert<TInput, TResult>(TInput input, TResult defaultValue) => defaultValue;

    public static int Combine<T1, T2>(T1 first, T2 second)
    {
        int hash1 = first!.GetHashCode();
        int hash2 = second!.GetHashCode();
        return hash1 ^ hash2;
    }
}

public interface IGenericContainer<T>
{
    T GetValue();
    void SetValue(T value);
}

public class GenericContainer<T> : IGenericContainer<T>
{
    private T _value;
    public GenericContainer(T value) { _value = value; }
    public T GetValue() => _value;
    public void SetValue(T value) => _value = value;
}

public delegate TResult GenericTransformer<TInput, TResult>(TInput input);

public class GenericOuter<T>
{
    public class Inner<U>
    {
        public T OuterValue;
        public U InnerValue;

        public int GetCombined()
        {
            int outerInt = (int)(object)OuterValue!;
            int innerLen = InnerValue!.ToString()!.Length;
            return outerInt + innerLen;
        }
    }
}

public static class GenericConstrainedMethods
{
    public static bool IsNull<T>(T value) where T : class => value == null;
    public static T GetDefault<T>() where T : struct => default(T);
    public static T CreateNew<T>() where T : new() => new T();
    public static int GetFromInterface<T>(T item) where T : IGenericValue => item.GetValue();
    public static int GetFromBase<T>(T item) where T : GenericBaseWithValue => item.GetBaseValue();
}

public interface IGenericValue { int GetValue(); }
public class GenericValueImpl : IGenericValue { public int GetValue() => 42; }
public class GenericCreatable { public int Value = 42; }
public class GenericBaseWithValue { public virtual int GetBaseValue() => 42; }
public class GenericDerivedWithValue : GenericBaseWithValue { public override int GetBaseValue() => 99; }

public interface IGenericCovariant<out T> { T Get(); }
public interface IGenericContravariant<in T> { void Accept(T item); }
public class GenericCovariantImpl<T> : IGenericCovariant<T>
{
    private T _value;
    public GenericCovariantImpl(T value) { _value = value; }
    public T Get() => _value;
}
public class GenericContravariantImpl<T> : IGenericContravariant<T>
{
    public void Accept(T item) { }
}
