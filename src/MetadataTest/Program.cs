// Comprehensive test assembly for metadata reader testing
// Exercises variety of metadata tables and signature formats

using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace MetadataTest;

// =============================================================================
// TypeDef varieties - classes, structs, interfaces, enums
// =============================================================================

public class BaseClass
{
    public virtual void VirtualMethod() { }
    protected int _protectedField;
}

public class DerivedClass : BaseClass, ITestInterface
{
    public override void VirtualMethod() { }
    public void InterfaceMethod() { }
}

public interface ITestInterface
{
    void InterfaceMethod();
}

// Interface for explicit implementation testing (generates MethodImpl table)
public interface IExplicitInterface
{
    void ExplicitMethod();
    int ExplicitProperty { get; }
}

// Class with explicit interface implementation - generates MethodImpl entries
public class ExplicitImplementation : IExplicitInterface
{
    // Explicit implementation - creates MethodImpl entry
    void IExplicitInterface.ExplicitMethod() { }
    int IExplicitInterface.ExplicitProperty => 42;
}

public interface IGenericInterface<T>
{
    T GetValue();
    void SetValue(T value);
}

public struct TestStruct
{
    public int X;
    public int Y;
    public readonly float Z;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct ExplicitLayoutStruct
{
    [FieldOffset(0)] public int A;
    [FieldOffset(4)] public int B;
    [FieldOffset(8)] public long C;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedStruct
{
    public byte A;
    public int B;
    public byte C;
}

public enum TestEnum
{
    None = 0,
    First = 1,
    Second = 2,
    Third = 3
}

[Flags]
public enum TestFlags : uint
{
    None = 0,
    FlagA = 1,
    FlagB = 2,
    FlagC = 4,
    All = FlagA | FlagB | FlagC
}

// =============================================================================
// Nested types
// =============================================================================

public class OuterClass
{
    public class NestedClass
    {
        public int Value;
    }

    private struct PrivateNestedStruct
    {
        public int X;
    }

    public enum NestedEnum { A, B, C }
}

// =============================================================================
// Generic types
// =============================================================================

public class GenericClass<T>
{
    private T _value;

    public T Value
    {
        get => _value;
        set => _value = value;
    }

    public void SetValue(T value) => _value = value;
}

public class GenericClass<T1, T2>
{
    public T1 First;
    public T2 Second;
}

public class ConstrainedGeneric<T> where T : class, new()
{
    public T CreateInstance() => new T();
}

public class StructConstrainedGeneric<T> where T : struct
{
    public T Value;
}

public struct GenericStruct<T>
{
    public T Value;
}

// =============================================================================
// Method signatures - various parameter types
// =============================================================================

public static class MethodSignatures
{
    // Primitives
    public static void Void() { }
    public static bool ReturnBool() => true;
    public static byte ReturnByte() => 0;
    public static sbyte ReturnSByte() => 0;
    public static short ReturnShort() => 0;
    public static ushort ReturnUShort() => 0;
    public static int ReturnInt() => 0;
    public static uint ReturnUInt() => 0;
    public static long ReturnLong() => 0;
    public static ulong ReturnULong() => 0;
    public static float ReturnFloat() => 0;
    public static double ReturnDouble() => 0;
    public static char ReturnChar() => '\0';
    public static string ReturnString() => "";
    public static object ReturnObject() => null!;
    public static IntPtr ReturnIntPtr() => IntPtr.Zero;
    public static UIntPtr ReturnUIntPtr() => UIntPtr.Zero;

    // Parameters
    public static void OneParam(int a) { }
    public static void TwoParams(int a, string b) { }
    public static void ThreeParams(int a, string b, double c) { }
    public static int AddInts(int a, int b) => a + b;

    // ref/out/in parameters
    public static void RefParam(ref int value) => value++;
    public static void OutParam(out int value) => value = 42;
    public static void InParam(in int value) { _ = value; }
    public static void MixedParams(int a, ref int b, out int c, in int d) { c = a + b + d; }

    // Arrays
    public static int[] ReturnArray() => Array.Empty<int>();
    public static int[,] ReturnMultiDimArray() => new int[1, 1];
    public static int[][] ReturnJaggedArray() => Array.Empty<int[]>();
    public static void ArrayParam(int[] arr) { }
    public static void ArrayParams(int[] a, string[] b, object[] c) { }

    // Generics
    public static T GenericMethod<T>(T value) => value;
    public static TResult GenericMethod<T, TResult>(T value) where TResult : new() => new TResult();
    public static void GenericConstraint<T>(T value) where T : IDisposable { }

    // Unsafe/pointers
    public static unsafe void PointerParam(int* ptr) { }
    public static unsafe int* ReturnPointer() => null;
    public static unsafe void VoidPointer(void* ptr) { }

    // Span (byref-like)
    public static void SpanParam(Span<int> span) { }
    public static void ReadOnlySpanParam(ReadOnlySpan<int> span) { }
    public static Span<int> ReturnSpan(int[] arr) => arr.AsSpan();
}

// =============================================================================
// Field signatures - various field types
// =============================================================================

public class FieldSignatures
{
    // Primitives
    public bool BoolField;
    public byte ByteField;
    public sbyte SByteField;
    public short ShortField;
    public ushort UShortField;
    public int IntField;
    public uint UIntField;
    public long LongField;
    public ulong ULongField;
    public float FloatField;
    public double DoubleField;
    public char CharField;
    public string StringField = "";
    public object ObjectField = null!;
    public IntPtr IntPtrField;
    public UIntPtr UIntPtrField;

    // Static fields
    public static int StaticInt;
    public static readonly int StaticReadonly = 42;
    public const int ConstInt = 100;

    // Arrays
    public int[] ArrayField = null!;
    public int[,] MultiDimField = null!;
    public int[][] JaggedField = null!;

    // Generic
    public GenericClass<int> GenericField = null!;
    public GenericClass<string, int> GenericField2 = null!;

    // Nullable
    public int? NullableInt;
    public TestStruct? NullableStruct;

    // Volatile
    public volatile int VolatileField;

    // Pointer (unsafe context)
    public unsafe int* PointerField;
}

// =============================================================================
// Properties and events
// =============================================================================

public class PropertiesAndEvents
{
    // Auto property
    public int AutoProperty { get; set; }

    // Read-only
    public int ReadOnly { get; }

    // Init-only
    public int InitOnly { get; init; }

    // Computed
    public int Computed => AutoProperty * 2;

    // Indexed property
    private int[] _items = new int[10];
    public int this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    // Static property
    public static int StaticProperty { get; set; }

    // Events
    public event EventHandler? SimpleEvent;
    public event EventHandler<EventArgs>? GenericEvent;

    // Custom event accessors
    private EventHandler? _customEvent;
    public event EventHandler? CustomEvent
    {
        add => _customEvent += value;
        remove => _customEvent -= value;
    }

    public void RaiseEvents()
    {
        SimpleEvent?.Invoke(this, EventArgs.Empty);
        GenericEvent?.Invoke(this, EventArgs.Empty);
        _customEvent?.Invoke(this, EventArgs.Empty);
    }
}

// =============================================================================
// Exception handling - generates EH clauses
// =============================================================================

public static class ExceptionHandling
{
    public static void TryCatch()
    {
        try
        {
            ThrowSomething();
        }
        catch (InvalidOperationException)
        {
            // catch specific
        }
        catch (Exception)
        {
            // catch general
        }
    }

    public static void TryFinally()
    {
        try
        {
            ThrowSomething();
        }
        finally
        {
            Cleanup();
        }
    }

    public static void TryCatchFinally()
    {
        try
        {
            ThrowSomething();
        }
        catch (Exception)
        {
            HandleError();
        }
        finally
        {
            Cleanup();
        }
    }

    public static void NestedTry()
    {
        try
        {
            try
            {
                ThrowSomething();
            }
            catch (ArgumentException)
            {
                // inner catch
            }
        }
        catch (Exception)
        {
            // outer catch
        }
    }

    public static void MultipleCatch()
    {
        try
        {
            ThrowSomething();
        }
        catch (ArgumentNullException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception)
        {
        }
    }

    private static void ThrowSomething() { }
    private static void HandleError() { }
    private static void Cleanup() { }
}

// =============================================================================
// Local variables - generates LocalVarSig
// =============================================================================

public static class LocalVariables
{
    public static void ManyLocals()
    {
        int a = 1;
        int b = 2;
        int c = 3;
        long d = 4;
        float e = 5.0f;
        double f = 6.0;
        string g = "test";
        object h = new object();
        int[] arr = new int[10];
        var list = new System.Collections.Generic.List<int>();

        // Use them to prevent optimization
        _ = a + b + c + d + e + f + g.Length + (h != null ? 1 : 0) + arr.Length + list.Count;
    }

    public static void TypedLocals()
    {
        TestStruct s = default;
        TestEnum e = TestEnum.First;
        GenericClass<int> g = new();
        BaseClass b = new DerivedClass();
        ITestInterface i = (ITestInterface)b;

        _ = s.X + (int)e + g.Value + (b != null ? 1 : 0) + (i != null ? 1 : 0);
    }
}

// =============================================================================
// P/Invoke - generates ImplMap table entries
// =============================================================================

public static class PInvokeMethods
{
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int printf(string format, __arglist);
}

// =============================================================================
// Custom attributes
// =============================================================================

[AttributeUsage(AttributeTargets.All)]
public class TestAttribute : Attribute
{
    public string Name { get; }
    public int Value { get; set; }

    public TestAttribute(string name) => Name = name;
}

[Test("ClassAttribute", Value = 42)]
public class AttributedClass
{
    [Test("FieldAttribute")]
    public int AttributedField;

    [Test("MethodAttribute")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void AttributedMethod([Test("ParamAttribute")] int param) { }

    [Test("PropertyAttribute")]
    public int AttributedProperty { get; set; }

    [return: Test("ReturnAttribute")]
    public int MethodWithReturnAttribute() => 0;
}

// =============================================================================
// Constant values - generates Constant table
// =============================================================================

public class Constants
{
    public const bool ConstBool = true;
    public const byte ConstByte = 255;
    public const sbyte ConstSByte = -128;
    public const short ConstShort = -32768;
    public const ushort ConstUShort = 65535;
    public const int ConstInt = -2147483648;
    public const uint ConstUInt = 4294967295;
    public const long ConstLong = -9223372036854775808;
    public const ulong ConstULong = 18446744073709551615;
    public const float ConstFloat = 3.14159f;
    public const double ConstDouble = 3.14159265358979;
    public const char ConstChar = 'X';
    public const string ConstString = "Hello, World!";

    public void MethodWithDefaults(
        int a = 42,
        string b = "default",
        double c = 3.14,
        bool d = true,
        object? e = null)
    { }
}

// =============================================================================
// User strings (#US heap)
// =============================================================================

public static class UserStrings
{
    public static string GetString1() => "Hello";
    public static string GetString2() => "World";
    public static string GetString3() => "The quick brown fox jumps over the lazy dog";
    public static string GetUnicode() => "\u4E2D\u6587"; // Chinese characters
    public static string GetEmpty() => "";
    public static string GetSpecial() => "Line1\nLine2\tTabbed\r\nCRLF";
}

// =============================================================================
// StandAloneSig - for calli and local vars
// =============================================================================

public static class StandAloneSigTests
{
    public static unsafe void CalliFunctionPointer()
    {
        delegate*<int, int, int> add = &Add;
        int result = add(1, 2);
        _ = result;
    }

    private static int Add(int a, int b) => a + b;
}

// =============================================================================
// TypeSpec - generic instantiations referenced
// =============================================================================

public static class TypeSpecTests
{
    public static void UseGenericTypes()
    {
        var list = new System.Collections.Generic.List<int>();
        var dict = new System.Collections.Generic.Dictionary<string, int>();
        var tuple = new Tuple<int, string>(1, "a");
        var nullable = new int?(42);

        list.Add(1);
        dict["key"] = 1;
        _ = tuple.Item1 + tuple.Item2.Length + nullable.Value;
    }
}

// =============================================================================
// MethodSpec - generic method instantiations
// =============================================================================

public static class MethodSpecTests
{
    public static T Identity<T>(T value) => value;
    public static TResult Convert<TInput, TResult>(TInput input, Func<TInput, TResult> converter) => converter(input);

    public static void CallGenericMethods()
    {
        _ = Identity(42);
        _ = Identity("hello");
        _ = Identity(3.14);
        _ = Identity<object>(new object());
        _ = Convert(42, x => x.ToString());
    }
}

// =============================================================================
// FieldRVA - static fields with RVA-based data
// =============================================================================

public static class FieldRvaTests
{
    // Static fields initialized with array data generate FieldRVA entries
    // The compiler stores the initial data in a special PE section
    public static readonly byte[] StaticByteArray = { 1, 2, 3, 4, 5, 6, 7, 8 };
    public static readonly int[] StaticIntArray = { 10, 20, 30, 40, 50 };
    public static readonly long[] StaticLongArray = { 100L, 200L, 300L };

    // Struct arrays with initial values also use FieldRVA
    public static readonly TestStruct[] StaticStructArray = {
        new TestStruct { X = 1, Y = 2 },
        new TestStruct { X = 3, Y = 4 }
    };

    public static int UseFields()
    {
        return StaticByteArray[0] + StaticIntArray[0] + (int)StaticLongArray[0] + StaticStructArray[0].X;
    }
}

// =============================================================================
// Entry point
// =============================================================================

public static class Program
{
    public static void Main()
    {
        // Entry point - just exists to make it a valid assembly
    }
}
