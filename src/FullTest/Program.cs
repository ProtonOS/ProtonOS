// FullTest - Comprehensive JIT Test Assembly for ProtonOS
// This assembly exercises all Tier 0 JIT functionality with real executable code paths.
// Each test method returns a result that can be validated by the kernel.

namespace FullTest;

/// <summary>
/// Main test runner class - entry point called by kernel JIT
/// </summary>
public static class TestRunner
{
    // Test result tracking
    private static int _passCount;
    private static int _failCount;

    /// <summary>
    /// Main entry point - runs all tests and returns pass/fail count
    /// Returns: (passCount << 16) | failCount
    /// </summary>
    public static int RunAllTests()
    {
        _passCount = 0;
        _failCount = 0;

        // Arithmetic tests
        RunArithmeticTests();

        // Comparison tests
        RunComparisonTests();

        // Bitwise operation tests
        RunBitwiseTests();

        // Control flow tests
        RunControlFlowTests();

        // Local variable tests
        RunLocalVariableTests();

        // Method call tests (includes recursion test)
        RunMethodCallTests();

        // Conversion tests
        RunConversionTests();

        // Object tests (if supported)
        RunObjectTests();

        // Array tests
        RunArrayTests();

        // Field tests
        RunFieldTests();

        // Struct tests - value type field access
        RunStructTests();

        // String tests - uses AOT method registry for String.get_Length, String.Concat
        RunStringTests();

        // TODO: Exception tests - requires MemberRef support (exception constructors from System.Runtime)
        // RunExceptionTests();

        // TODO: Generic tests - requires MethodSpec/TypeSpec support
        // RunGenericTests();

        return (_passCount << 16) | _failCount;
    }

    private static void RunStringTests()
    {
        RecordResult(StringTests.TestLdstr() == 5);
        RecordResult(StringTests.TestStringConcat() == 10);
    }

    private static void RunExceptionTests()
    {
        RecordResult(ExceptionTests.TestTryCatch() == 42);
        RecordResult(ExceptionTests.TestTryFinally() == 42);
        RecordResult(ExceptionTests.TestNestedTryCatch() == 42);
    }

    private static void RunGenericTests()
    {
        RecordResult(GenericTests.TestGenericMethod() == 42);
        RecordResult(GenericTests.TestGenericClass() == 42);
    }

    private static void RecordResult(bool passed)
    {
        if (passed)
            _passCount++;
        else
            _failCount++;
    }

    private static void RunArithmeticTests()
    {
        RecordResult(ArithmeticTests.TestAdd() == 42);
        RecordResult(ArithmeticTests.TestSub() == 8);
        RecordResult(ArithmeticTests.TestMul() == 56);
        RecordResult(ArithmeticTests.TestDiv() == 5);
        RecordResult(ArithmeticTests.TestRem() == 3);
        RecordResult(ArithmeticTests.TestNeg() == -42);
        RecordResult(ArithmeticTests.TestAddOverflow() == unchecked((int)0x80000001));
        RecordResult(ArithmeticTests.TestLongAdd() == 0x1_0000_0000L);
        RecordResult(ArithmeticTests.TestLongMul() == 0x10_0000_0000L);
    }

    private static void RunComparisonTests()
    {
        RecordResult(ComparisonTests.TestCeq() == 1);
        RecordResult(ComparisonTests.TestCgt() == 1);
        RecordResult(ComparisonTests.TestClt() == 1);
        RecordResult(ComparisonTests.TestCgtUn() == 1);
        RecordResult(ComparisonTests.TestCltUn() == 1);
        RecordResult(ComparisonTests.TestEqualsFalse() == 0);
    }

    private static void RunBitwiseTests()
    {
        RecordResult(BitwiseTests.TestAnd() == 0x10);
        RecordResult(BitwiseTests.TestOr() == 0xFF);
        RecordResult(BitwiseTests.TestXor() == 0xEF);
        RecordResult(BitwiseTests.TestNot() == unchecked((int)0xFFFFFFF0));
        RecordResult(BitwiseTests.TestShl() == 0x80);
        RecordResult(BitwiseTests.TestShr() == 0x04);
        RecordResult(BitwiseTests.TestShrUn() == 0x7FFFFFFF);
    }

    private static void RunControlFlowTests()
    {
        RecordResult(ControlFlowTests.TestBranch() == 100);
        RecordResult(ControlFlowTests.TestBrtrue() == 1);
        RecordResult(ControlFlowTests.TestBrfalse() == 1);
        RecordResult(ControlFlowTests.TestBeq() == 1);
        RecordResult(ControlFlowTests.TestBne() == 1);
        RecordResult(ControlFlowTests.TestBgt() == 1);
        RecordResult(ControlFlowTests.TestBlt() == 1);
        RecordResult(ControlFlowTests.TestBge() == 1);
        RecordResult(ControlFlowTests.TestBle() == 1);
        RecordResult(ControlFlowTests.TestLoop() == 55);  // Sum of 1..10
        RecordResult(ControlFlowTests.TestNestedLoop() == 100);
        RecordResult(ControlFlowTests.TestSwitch() == 42);
    }

    private static void RunLocalVariableTests()
    {
        RecordResult(LocalVariableTests.TestSimpleLocals() == 30);
        RecordResult(LocalVariableTests.TestManyLocals() == 45);
        RecordResult(LocalVariableTests.TestLocalAddress() == 42);
    }

    private static void RunMethodCallTests()
    {
        RecordResult(MethodCallTests.TestSimpleCall() == 42);
        RecordResult(MethodCallTests.TestCallWithArgs() == 15);
        RecordResult(MethodCallTests.TestCallChain() == 120);
        RecordResult(MethodCallTests.TestRecursion() == 120);  // 5! = 120
    }

    private static void RunConversionTests()
    {
        RecordResult(ConversionTests.TestConvI4() == 42);
        RecordResult(ConversionTests.TestConvI8() == 0x1_0000_0000L);
        RecordResult(ConversionTests.TestConvU4() == 0xFFFFFFFF);
        RecordResult(ConversionTests.TestConvI1() == -1);
        RecordResult(ConversionTests.TestConvU1() == 255);
        RecordResult(ConversionTests.TestConvI2() == -1);
        RecordResult(ConversionTests.TestConvU2() == 65535);
    }

    private static void RunObjectTests()
    {
        RecordResult(ObjectTests.TestLdnull() == 1);
    }

    private static void RunArrayTests()
    {
        RecordResult(ArrayTests.TestNewarr() == 10);
        RecordResult(ArrayTests.TestStelem() == 42);
        RecordResult(ArrayTests.TestLdlen() == 5);
        RecordResult(ArrayTests.TestArraySum() == 15);
    }

    private static void RunFieldTests()
    {
        RecordResult(FieldTests.TestStaticField() == 42);
        RecordResult(FieldTests.TestStaticFieldIncrement() == 43);
        RecordResult(FieldTests.TestMultipleStaticFields() == 100);
    }

    private static void RunStructTests()
    {
        // Basic struct field access tests
        RecordResult(StructTests.TestStructLocalFieldWrite() == 42);
        RecordResult(StructTests.TestStructLocalFieldSum() == 30);
        RecordResult(StructTests.TestStructPassByValue() == 100);
        RecordResult(StructTests.TestStructOutParam() == 42);

        // Object initializer tests
        RecordResult(StructTests.TestObjectInitializer() == 30);
        RecordResult(StructTests.TestStindI8() == 100);  // Test stind.i8 pattern first
        RecordResult(StructTests.TestSimpleStobj() == 300);  // RE-ENABLED for debugging
        RecordResult(StructTests.TestObjectInitializerOut() == 30);  // TODO: Fix initobj+stobj interaction

        // Struct array tests (ldelem/stelem with type token)
        RecordResult(StructTests.TestStructArrayStore() == 10);
        RecordResult(StructTests.TestStructArrayLoad() == 70);
        RecordResult(StructTests.TestStructArrayCopy() == 100);
        RecordResult(StructTests.TestStructArrayMultiple() == 111);

        // Large struct tests (structs > 8 bytes)
        RecordResult(StructTests.TestLargeStructFields() == 60);
        RecordResult(StructTests.TestLargeStructCopy() == 600);
        RecordResult(StructTests.TestLargeStructArrayStore() == 1);  // Struct first, array second
        RecordResult(StructTests.TestLargeStructArrayLoad() == 60);
        RecordResult(StructTests.TestLargeStructArrayCopy() == 100);
    }
}

// =============================================================================
// Arithmetic Tests
// =============================================================================

public static class ArithmeticTests
{
    public static int TestAdd()
    {
        int a = 17;
        int b = 25;
        return a + b;  // 42
    }

    public static int TestSub()
    {
        int a = 20;
        int b = 12;
        return a - b;  // 8
    }

    public static int TestMul()
    {
        int a = 7;
        int b = 8;
        return a * b;  // 56
    }

    public static int TestDiv()
    {
        int a = 25;
        int b = 5;
        return a / b;  // 5
    }

    public static int TestRem()
    {
        int a = 23;
        int b = 5;
        return a % b;  // 3
    }

    public static int TestNeg()
    {
        int a = 42;
        return -a;  // -42
    }

    public static int TestAddOverflow()
    {
        // Test overflow behavior (unchecked by default)
        int a = int.MaxValue;  // 0x7FFFFFFF
        int b = 2;
        return a + b;  // 0x80000001 (wraps to negative)
    }

    public static long TestLongAdd()
    {
        long a = 0x80000000L;
        long b = 0x80000000L;
        return a + b;  // 0x1_0000_0000
    }

    public static long TestLongMul()
    {
        long a = 0x4_0000_0000L;
        long b = 4;
        return a * b;  // 0x10_0000_0000
    }
}

// =============================================================================
// Comparison Tests
// =============================================================================

public static class ComparisonTests
{
    public static int TestCeq()
    {
        int a = 42;
        int b = 42;
        return a == b ? 1 : 0;
    }

    public static int TestCgt()
    {
        int a = 50;
        int b = 42;
        return a > b ? 1 : 0;
    }

    public static int TestClt()
    {
        int a = 30;
        int b = 42;
        return a < b ? 1 : 0;
    }

    public static int TestCgtUn()
    {
        uint a = 0xFFFFFFFF;  // Large unsigned value
        uint b = 1;
        return a > b ? 1 : 0;
    }

    public static int TestCltUn()
    {
        uint a = 1;
        uint b = 0xFFFFFFFF;  // Large unsigned value
        return a < b ? 1 : 0;
    }

    public static int TestEqualsFalse()
    {
        int a = 42;
        int b = 43;
        return a == b ? 1 : 0;
    }
}

// =============================================================================
// Bitwise Operation Tests
// =============================================================================

public static class BitwiseTests
{
    public static int TestAnd()
    {
        int a = 0x1F;
        int b = 0xF0;
        return a & b;  // 0x10
    }

    public static int TestOr()
    {
        int a = 0x0F;
        int b = 0xF0;
        return a | b;  // 0xFF
    }

    public static int TestXor()
    {
        int a = 0xFF;
        int b = 0x10;
        return a ^ b;  // 0xEF
    }

    public static int TestNot()
    {
        int a = 0x0F;
        return ~a;  // 0xFFFFFFF0
    }

    public static int TestShl()
    {
        int a = 0x08;
        int shift = 4;
        return a << shift;  // 0x80
    }

    public static int TestShr()
    {
        int a = 0x40;
        int shift = 4;
        return a >> shift;  // 0x04
    }

    public static int TestShrUn()
    {
        // Unsigned right shift (logical shift)
        int a = unchecked((int)0xFFFFFFFE);  // -2 as signed
        return (int)((uint)a >> 1);  // 0x7FFFFFFF
    }
}

// =============================================================================
// Control Flow Tests
// =============================================================================

public static class ControlFlowTests
{
    public static int TestBranch()
    {
        int x = 100;
        goto end;
        x = 200;  // Never executed
    end:
        return x;
    }

    public static int TestBrtrue()
    {
        int x = 1;
        if (x != 0)
            return 1;
        return 0;
    }

    public static int TestBrfalse()
    {
        int x = 0;
        if (x == 0)
            return 1;
        return 0;
    }

    public static int TestBeq()
    {
        int a = 42;
        int b = 42;
        if (a == b)
            return 1;
        return 0;
    }

    public static int TestBne()
    {
        int a = 42;
        int b = 43;
        if (a != b)
            return 1;
        return 0;
    }

    public static int TestBgt()
    {
        int a = 50;
        int b = 42;
        if (a > b)
            return 1;
        return 0;
    }

    public static int TestBlt()
    {
        int a = 30;
        int b = 42;
        if (a < b)
            return 1;
        return 0;
    }

    public static int TestBge()
    {
        int a = 42;
        int b = 42;
        if (a >= b)
            return 1;
        return 0;
    }

    public static int TestBle()
    {
        int a = 42;
        int b = 50;
        if (a <= b)
            return 1;
        return 0;
    }

    public static int TestLoop()
    {
        // Sum 1 to 10 = 55
        int sum = 0;
        for (int i = 1; i <= 10; i++)
        {
            sum += i;
        }
        return sum;
    }

    public static int TestNestedLoop()
    {
        // 10 * 10 = 100
        int count = 0;
        for (int i = 0; i < 10; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                count++;
            }
        }
        return count;
    }

    public static int TestSwitch()
    {
        int x = 2;
        switch (x)
        {
            case 0: return 10;
            case 1: return 20;
            case 2: return 42;
            case 3: return 50;
            default: return 0;
        }
    }
}

// =============================================================================
// Local Variable Tests
// =============================================================================

public static class LocalVariableTests
{
    public static int TestSimpleLocals()
    {
        int a = 10;
        int b = 20;
        return a + b;  // 30
    }

    public static int TestManyLocals()
    {
        // Test that we can handle many local variables
        int v0 = 0, v1 = 1, v2 = 2, v3 = 3, v4 = 4;
        int v5 = 5, v6 = 6, v7 = 7, v8 = 8, v9 = 9;
        return v0 + v1 + v2 + v3 + v4 + v5 + v6 + v7 + v8 + v9;  // 45
    }

    public static unsafe int TestLocalAddress()
    {
        int x = 42;
        int* ptr = &x;
        return *ptr;  // 42
    }
}

// =============================================================================
// Method Call Tests
// =============================================================================

public static class MethodCallTests
{
    public static int TestSimpleCall()
    {
        return GetFortyTwo();
    }

    private static int GetFortyTwo()
    {
        return 42;
    }

    public static int TestCallWithArgs()
    {
        return Add(5, 10);
    }

    private static int Add(int a, int b)
    {
        return a + b;
    }

    public static int TestCallChain()
    {
        return A(10);
    }

    private static int A(int x) => B(x * 2);
    private static int B(int x) => C(x * 3);
    private static int C(int x) => x * 2;  // 10 * 2 * 3 * 2 = 120

    public static int TestRecursion()
    {
        return Factorial(5);  // 5! = 120
    }

    private static int Factorial(int n)
    {
        if (n <= 1)
            return 1;
        return n * Factorial(n - 1);
    }
}

// =============================================================================
// Conversion Tests
// =============================================================================

public static class ConversionTests
{
    public static int TestConvI4()
    {
        long x = 42L;
        return (int)x;
    }

    public static long TestConvI8()
    {
        int x = unchecked((int)0x1_0000_0000L);  // Truncates, but we want to test conv.i8
        // Use a different approach
        uint high = 1;
        uint low = 0;
        return ((long)high << 32) | low;
    }

    public static uint TestConvU4()
    {
        long x = 0xFFFFFFFFL;
        return (uint)x;
    }

    public static sbyte TestConvI1()
    {
        int x = 0xFF;
        return (sbyte)x;  // -1 (truncation + sign extension)
    }

    public static byte TestConvU1()
    {
        int x = 0xFFFF;
        return (byte)x;  // 255 (truncation)
    }

    public static short TestConvI2()
    {
        int x = 0xFFFF;
        return (short)x;  // -1
    }

    public static ushort TestConvU2()
    {
        int x = 0xFFFFFF;
        return (ushort)x;  // 65535
    }
}

// =============================================================================
// Object Tests
// =============================================================================

public static class ObjectTests
{
    public static int TestLdnull()
    {
        object? obj = null;
        return obj == null ? 1 : 0;
    }
}

// =============================================================================
// Array Tests
// =============================================================================

public static class ArrayTests
{
    public static int TestNewarr()
    {
        int[] arr = new int[10];
        return arr.Length;
    }

    public static int TestStelem()
    {
        int[] arr = new int[1];
        arr[0] = 42;
        return arr[0];
    }

    public static int TestLdlen()
    {
        int[] arr = new int[5];
        return arr.Length;
    }

    public static int TestArraySum()
    {
        int[] arr = new int[5];
        arr[0] = 1;
        arr[1] = 2;
        arr[2] = 3;
        arr[3] = 4;
        arr[4] = 5;

        int sum = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            sum += arr[i];
        }
        return sum;  // 15
    }
}

// =============================================================================
// Field Tests
// =============================================================================

public static class FieldTests
{
    private static int _staticField;
    private static int _fieldA;
    private static int _fieldB;

    public static int TestStaticField()
    {
        _staticField = 42;
        return _staticField;
    }

    public static int TestStaticFieldIncrement()
    {
        _staticField = 42;
        _staticField++;
        return _staticField;  // 43
    }

    public static int TestMultipleStaticFields()
    {
        _fieldA = 40;
        _fieldB = 60;
        return _fieldA + _fieldB;  // 100
    }
}

// =============================================================================
// Instance Field and Object Tests (for later phases)
// =============================================================================

public class SimpleClass
{
    public int Value;

    public SimpleClass(int value)
    {
        Value = value;
    }

    public int GetValue()
    {
        return Value;
    }

    public void SetValue(int value)
    {
        Value = value;
    }
}

public struct SimpleStruct
{
    public int X;
    public int Y;

    public SimpleStruct(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int Sum()
    {
        return X + Y;
    }
}

// Large struct for testing > 8 byte struct operations (24 bytes)
public struct LargeStruct
{
    public long A;
    public long B;
    public long C;
}

// =============================================================================
// Struct Tests - Value Type Field Access
// =============================================================================

public static class StructTests
{
    // Test writing to a struct field via ldloca
    public static int TestStructLocalFieldWrite()
    {
        SimpleStruct s;
        s.X = 42;
        s.Y = 0;
        return s.X;  // 42
    }

    // Test reading both fields
    public static int TestStructLocalFieldSum()
    {
        SimpleStruct s;
        s.X = 10;
        s.Y = 20;
        return s.X + s.Y;  // 30
    }

    // Test passing struct by value to a method
    public static int TestStructPassByValue()
    {
        SimpleStruct s;
        s.X = 40;
        s.Y = 60;
        return GetStructSum(s);  // 100
    }

    private static int GetStructSum(SimpleStruct s)
    {
        return s.X + s.Y;
    }

    // Test struct out parameter
    public static int TestStructOutParam()
    {
        SimpleStruct s;
        InitStruct(out s);
        return s.X;  // 42
    }

    private static void InitStruct(out SimpleStruct s)
    {
        // Use explicit assignment instead of object initializer (JIT workaround)
        s = default;
        s.X = 42;
        s.Y = 0;
    }

    // Test object initializer syntax (dup + initobj + stfld sequence)
    // This tests the TOS caching bug with dup followed by initobj
    public static int TestObjectInitializer()
    {
        // This generates: ldloca, dup, initobj, ldc.i4, stfld, ldc.i4, stfld, pop
        SimpleStruct s = new SimpleStruct { X = 10, Y = 20 };
        return s.X + s.Y;  // Expected: 30
    }

    // Test object initializer with out parameter (more complex)
    public static int TestObjectInitializerOut()
    {
        SimpleStruct s;
        InitStructWithInitializer(out s);
        return s.X + s.Y;  // Expected: 30
    }

    private static void InitStructWithInitializer(out SimpleStruct s)
    {
        // This uses object initializer syntax on an out parameter
        // Generates: ldarg.0, ldloca.s 0, initobj, ldloca.s 0, ldc.i4, stfld,
        //            ldloca.s 0, ldc.i4, stfld, ldloc.0, stobj
        s = new SimpleStruct { X = 10, Y = 20 };
    }

    // Test simple stobj without object initializer complexity
    public static int TestSimpleStobj()
    {
        SimpleStruct src;
        src.X = 100;
        src.Y = 200;
        SimpleStruct dest;
        CopyStructToOut(out dest, src);
        return dest.X + dest.Y;  // Expected: 300
    }

    private static void CopyStructToOut(out SimpleStruct dest, SimpleStruct src)
    {
        // This should generate: ldarg.0, ldarg.1, stobj
        // Simpler stobj test without initobj/temp local complexity
        dest = src;
    }

    // Test stind.i8 (for long out param) - same IL pattern as stobj
    public static int TestStindI8()
    {
        long src = 0x0000006400000064;  // 100 in both halves
        long dest;
        CopyLongToOut(out dest, src);
        return (int)(dest & 0xFFFFFFFF);  // Expected: 100
    }

    private static void CopyLongToOut(out long dest, long src)
    {
        // This should generate: ldarg.0, ldarg.1, stind.i8, ret
        // Same pattern as stobj but for primitive type
        dest = src;
    }

    // =========================================================================
    // Struct Array Tests - Tests ldelem/stelem with value type token
    // =========================================================================

    // Test storing struct to array (stelem with type token)
    public static int TestStructArrayStore()
    {
        SimpleStruct[] arr = new SimpleStruct[2];
        SimpleStruct s;
        s.X = 10;
        s.Y = 20;
        arr[0] = s;  // stelem with SimpleStruct token
        return arr[0].X;  // Expected: 10
    }

    // Test loading struct from array (ldelem with type token)
    public static int TestStructArrayLoad()
    {
        SimpleStruct[] arr = new SimpleStruct[2];
        arr[0].X = 30;
        arr[0].Y = 40;
        SimpleStruct loaded = arr[0];  // ldelem with SimpleStruct token
        return loaded.X + loaded.Y;  // Expected: 70
    }

    // Test that loading from array creates a copy (not a reference)
    public static int TestStructArrayCopy()
    {
        SimpleStruct[] arr = new SimpleStruct[1];
        arr[0].X = 100;
        arr[0].Y = 200;

        SimpleStruct copy = arr[0];  // ldelem - should copy
        copy.X = 999;  // Modify copy

        return arr[0].X;  // Expected: 100 (original unchanged)
    }

    // Test struct array with multiple elements
    public static int TestStructArrayMultiple()
    {
        SimpleStruct[] arr = new SimpleStruct[3];

        SimpleStruct s0;
        s0.X = 1;
        s0.Y = 2;
        arr[0] = s0;

        SimpleStruct s1;
        s1.X = 10;
        s1.Y = 20;
        arr[1] = s1;

        SimpleStruct s2;
        s2.X = 100;
        s2.Y = 200;
        arr[2] = s2;

        return arr[0].X + arr[1].X + arr[2].X;  // Expected: 1 + 10 + 100 = 111
    }

    // =========================================================================
    // Large Struct Tests - Tests structs > 8 bytes
    // =========================================================================

    // Test large struct field access
    public static int TestLargeStructFields()
    {
        LargeStruct ls;
        ls.A = 10;
        ls.B = 20;
        ls.C = 30;
        return (int)(ls.A + ls.B + ls.C);  // Expected: 60
    }

    // Test large struct copy via stobj
    public static int TestLargeStructCopy()
    {
        LargeStruct src;
        src.A = 100;
        src.B = 200;
        src.C = 300;

        LargeStruct dest;
        CopyLargeStructToOut(out dest, src);
        return (int)(dest.A + dest.B + dest.C);  // Expected: 600
    }

    private static void CopyLargeStructToOut(out LargeStruct dest, LargeStruct src)
    {
        dest = src;  // stobj for large struct
    }

    // Test large struct array store (swapped order to diagnose local parsing)
    public static int TestLargeStructArrayStore()
    {
        LargeStruct ls;  // local 0: struct (value type)
        LargeStruct[] arr = new LargeStruct[2];  // local 1: array (ref type)
        ls.A = 1;
        ls.B = 2;
        ls.C = 3;
        arr[0] = ls;  // stelem for large struct
        return (int)arr[0].A;  // Expected: 1
    }

    // Test large struct array load
    public static int TestLargeStructArrayLoad()
    {
        LargeStruct[] arr = new LargeStruct[2];
        arr[0].A = 10;
        arr[0].B = 20;
        arr[0].C = 30;
        LargeStruct loaded = arr[0];  // ldelem for large struct
        return (int)(loaded.A + loaded.B + loaded.C);  // Expected: 60
    }

    // Test large struct array copy semantics
    public static int TestLargeStructArrayCopy()
    {
        LargeStruct[] arr = new LargeStruct[1];
        arr[0].A = 100;
        arr[0].B = 200;
        arr[0].C = 300;

        LargeStruct copy = arr[0];  // ldelem - should copy
        copy.A = 999;  // Modify copy

        return (int)arr[0].A;  // Expected: 100 (original unchanged)
    }
}

// =============================================================================
// String Tests (requires ldstr support)
// =============================================================================

public static class StringTests
{
    public static int TestLdstr()
    {
        string s = "Hello";
        return s.Length;  // 5
    }

    public static int TestStringConcat()
    {
        string a = "Hello";
        string b = "World";
        string c = a + b;
        return c.Length;  // 10
    }
}

// =============================================================================
// Exception Handling Tests (for later phases)
// =============================================================================

public static class ExceptionTests
{
    public static int TestTryCatch()
    {
        try
        {
            ThrowException();
            return 0;  // Should not reach here
        }
        catch
        {
            return 42;  // Exception caught
        }
    }

    private static void ThrowException()
    {
        throw new InvalidOperationException("Test exception");
    }

    public static int TestTryFinally()
    {
        int result = 0;
        try
        {
            result = 10;
        }
        finally
        {
            result += 32;
        }
        return result;  // 42
    }

    public static int TestNestedTryCatch()
    {
        try
        {
            try
            {
                throw new InvalidOperationException("Inner");
            }
            catch (InvalidOperationException)
            {
                throw new ArgumentException("Outer");
            }
        }
        catch (ArgumentException)
        {
            return 42;
        }
        return 0;
    }
}

// =============================================================================
// Generic Tests (for later phases)
// =============================================================================

public static class GenericTests
{
    public static int TestGenericMethod()
    {
        return Identity(42);
    }

    private static T Identity<T>(T value)
    {
        return value;
    }

    public static int TestGenericClass()
    {
        var box = new Box<int>(42);
        return box.Value;
    }
}

public class Box<T>
{
    public T Value { get; }

    public Box(T value)
    {
        Value = value;
    }
}

// =============================================================================
// Entry Point (required for valid assembly)
// =============================================================================

public class Program
{
    public static void Main()
    {
        // This Main is required for the assembly to be valid.
        // The kernel will call TestRunner.RunAllTests() directly via JIT.
        TestRunner.RunAllTests();
    }
}
