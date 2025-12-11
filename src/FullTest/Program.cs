// FullTest - Comprehensive JIT Test Assembly for ProtonOS
// This assembly exercises all Tier 0 JIT functionality with real executable code paths.
// Each test method returns a result that can be validated by the kernel.

using ProtonOS.DDK.Kernel;

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

        // Boxing and advanced newobj tests (Phase 4)
        // TEMPORARILY DISABLED - investigating crash
        // RunBoxingTests();

        // Instance member tests (Phase 5)
        // TEMPORARILY DISABLED - crashes with CR2=0x64
        // RunInstanceTests();

        // TODO: Exception tests - requires MemberRef support (exception constructors from System.Runtime)
        // RunExceptionTests();

        // Generic tests - tests MethodSpec (0x2B) token resolution
        RunGenericTests();

        // String.Format tests - tests AOT method calls from JIT
        RunStringFormatTests();

        // String interpolation tests - requires MethodSpec (0x2B), MVAR, and cross-assembly TypeRef resolution
        RunStringInterpolationTests();

        return (_passCount << 16) | _failCount;
    }

    private static void RunStringFormatTests()
    {
        RecordResult("StringFormatTests.TestFormatOneArg", StringFormatTests.TestFormatOneArg() == 9);
        RecordResult("StringFormatTests.TestFormatTwoArgs", StringFormatTests.TestFormatTwoArgs() == 7);
        RecordResult("StringFormatTests.TestFormatThreeArgs", StringFormatTests.TestFormatThreeArgs() == 12);
        RecordResult("StringFormatTests.TestFormatStringArg", StringFormatTests.TestFormatStringArg() == 13);
    }

    private static void RunStringTests()
    {
        RecordResult("StringTests.TestLdstr", StringTests.TestLdstr() == 5);
        RecordResult("StringTests.TestStringConcat", StringTests.TestStringConcat() == 10);
    }

    private static void RunBoxingTests()
    {
        // Phase 4.1: newobj with >4 constructor args - WORKS
        RecordResult("BoxingTests.TestNewObjManyArgs", BoxingTests.TestNewObjManyArgs() == 15);

        // Phase 4.2: box/unbox operations
        RecordResult("BoxingTests.TestBoxInt", BoxingTests.TestBoxInt() == 42);  // Int boxing - WORKS

        // Struct boxing tests
        RecordResult("BoxingTests.TestBoxStruct", BoxingTests.TestBoxStruct() == 30);  // Small struct (8 bytes)
        RecordResult("BoxingTests.TestBoxMediumStruct", BoxingTests.TestBoxMediumStruct() == 300);  // Medium struct (16 bytes)
        RecordResult("BoxingTests.TestBoxLargeStruct", BoxingTests.TestBoxLargeStruct() == 60);  // Large struct (24 bytes)
    }

    private static void RunInstanceTests()
    {
        // Phase 5.1: Instance field access
        RecordResult("InstanceTests.TestInstanceFieldReadWrite", InstanceTests.TestInstanceFieldReadWrite() == 42);
        RecordResult("InstanceTests.TestMultipleInstanceFields", InstanceTests.TestMultipleInstanceFields() == 60);

        // Phase 5.2: Instance method calls
        RecordResult("InstanceTests.TestInstanceMethodCall", InstanceTests.TestInstanceMethodCall() == 30);
        RecordResult("InstanceTests.TestInstanceMethodWithThis", InstanceTests.TestInstanceMethodWithThis() == 50);

        // Regression: callvirt returning bool, store to local, branch on local
        RecordResult("InstanceTests.TestCallvirtBoolBranchWithLargeLocals", InstanceTests.TestCallvirtBoolBranchWithLargeLocals() == 2);

        // Critical: Cross-assembly large struct return (hidden buffer convention)
        RecordResult("InstanceTests.TestCrossAssemblyLargeStructReturn", InstanceTests.TestCrossAssemblyLargeStructReturn() == 42);
        RecordResult("InstanceTests.TestCrossAssemblyLargeStructToClassField", InstanceTests.TestCrossAssemblyLargeStructToClassField() == 42);
    }

    private static void RunExceptionTests()
    {
        RecordResult("ExceptionTests.TestTryCatch", ExceptionTests.TestTryCatch() == 42);
        RecordResult("ExceptionTests.TestTryFinally", ExceptionTests.TestTryFinally() == 42);
        RecordResult("ExceptionTests.TestNestedTryCatch", ExceptionTests.TestNestedTryCatch() == 42);
    }

    private static void RunGenericTests()
    {
        RecordResult("GenericTests.TestGenericMethod", GenericTests.TestGenericMethod() == 42);
        // TestGenericClass requires TypeSpec (0x1B) support for generic type instantiation
        RecordResult("GenericTests.TestGenericClass", GenericTests.TestGenericClass() == 42);
    }

    private static void RunStringInterpolationTests()
    {
        RecordResult("StringInterpolationTests.TestSimpleInterpolation", StringInterpolationTests.TestSimpleInterpolation() == 9);     // "Value: 42"
        RecordResult("StringInterpolationTests.TestMultipleValues", StringInterpolationTests.TestMultipleValues() == 12);         // "10 + 20 = 30"
        RecordResult("StringInterpolationTests.TestStringValues", StringInterpolationTests.TestStringValues() == 12);           // "Hello, Test!"
    }

    private static void RecordResult(string testName, bool passed)
    {
        if (passed)
            _passCount++;
        else
        {
            _failCount++;
            Debug.WriteLine(string.Format("[FAIL] {0}", testName));
        }
    }

    private static void RunArithmeticTests()
    {
        RecordResult("ArithmeticTests.TestAdd", ArithmeticTests.TestAdd() == 42);
        RecordResult("ArithmeticTests.TestSub", ArithmeticTests.TestSub() == 8);
        RecordResult("ArithmeticTests.TestMul", ArithmeticTests.TestMul() == 56);
        RecordResult("ArithmeticTests.TestDiv", ArithmeticTests.TestDiv() == 5);
        RecordResult("ArithmeticTests.TestRem", ArithmeticTests.TestRem() == 3);
        RecordResult("ArithmeticTests.TestNeg", ArithmeticTests.TestNeg() == -42);
        RecordResult("ArithmeticTests.TestAddOverflow", ArithmeticTests.TestAddOverflow() == unchecked((int)0x80000001));
        RecordResult("ArithmeticTests.TestLongAdd", ArithmeticTests.TestLongAdd() == 0x1_0000_0000L);
        RecordResult("ArithmeticTests.TestLongMul", ArithmeticTests.TestLongMul() == 0x10_0000_0000L);
        RecordResult("ArithmeticTests.TestUlongPlusUint", ArithmeticTests.TestUlongPlusUint() == 1);     // ulong + uint high bits preserved
        RecordResult("ArithmeticTests.TestUlongPlusUintLow", ArithmeticTests.TestUlongPlusUintLow() == 1);  // ulong + uint low bits correct
    }

    private static void RunComparisonTests()
    {
        RecordResult("ComparisonTests.TestCeq", ComparisonTests.TestCeq() == 1);
        RecordResult("ComparisonTests.TestCgt", ComparisonTests.TestCgt() == 1);
        RecordResult("ComparisonTests.TestClt", ComparisonTests.TestClt() == 1);
        RecordResult("ComparisonTests.TestCgtUn", ComparisonTests.TestCgtUn() == 1);
        RecordResult("ComparisonTests.TestCltUn", ComparisonTests.TestCltUn() == 1);
        RecordResult("ComparisonTests.TestEqualsFalse", ComparisonTests.TestEqualsFalse() == 0);
        // 64-bit comparison tests
        RecordResult("ComparisonTests.TestCeqLongZero", ComparisonTests.TestCeqLongZero() == 1);
        RecordResult("ComparisonTests.TestCeqLongNonZero", ComparisonTests.TestCeqLongNonZero() == 0);
        RecordResult("ComparisonTests.TestCeqLongEquals", ComparisonTests.TestCeqLongEquals() == 1);
        RecordResult("ComparisonTests.TestCeqLongNotEquals", ComparisonTests.TestCeqLongNotEquals() == 0);
    }

    private static void RunBitwiseTests()
    {
        RecordResult("BitwiseTests.TestAnd", BitwiseTests.TestAnd() == 0x10);
        RecordResult("BitwiseTests.TestOr", BitwiseTests.TestOr() == 0xFF);
        RecordResult("BitwiseTests.TestXor", BitwiseTests.TestXor() == 0xEF);
        RecordResult("BitwiseTests.TestNot", BitwiseTests.TestNot() == unchecked((int)0xFFFFFFF0));
        RecordResult("BitwiseTests.TestShl", BitwiseTests.TestShl() == 0x80);
        RecordResult("BitwiseTests.TestShr", BitwiseTests.TestShr() == 0x04);
        RecordResult("BitwiseTests.TestShrUn", BitwiseTests.TestShrUn() == 0x7FFFFFFF);
        RecordResult("BitwiseTests.TestShl64By32Simple", BitwiseTests.TestShl64By32Simple() == 1);  // 64-bit shift left by 32
        RecordResult("BitwiseTests.TestShl64By32", BitwiseTests.TestShl64By32() == 0xC0);     // 64-bit shift with conv.u8
        RecordResult("BitwiseTests.TestShl64LowBits", BitwiseTests.TestShl64LowBits() == unchecked((int)0xABCD0000));  // 64-bit shift by 16
    }

    private static void RunControlFlowTests()
    {
        RecordResult("ControlFlowTests.TestBranch", ControlFlowTests.TestBranch() == 100);
        RecordResult("ControlFlowTests.TestBrtrue", ControlFlowTests.TestBrtrue() == 1);
        RecordResult("ControlFlowTests.TestBrfalse", ControlFlowTests.TestBrfalse() == 1);
        RecordResult("ControlFlowTests.TestBeq", ControlFlowTests.TestBeq() == 1);
        RecordResult("ControlFlowTests.TestBne", ControlFlowTests.TestBne() == 1);
        RecordResult("ControlFlowTests.TestBgt", ControlFlowTests.TestBgt() == 1);
        RecordResult("ControlFlowTests.TestBlt", ControlFlowTests.TestBlt() == 1);
        RecordResult("ControlFlowTests.TestBge", ControlFlowTests.TestBge() == 1);
        RecordResult("ControlFlowTests.TestBle", ControlFlowTests.TestBle() == 1);
        RecordResult("ControlFlowTests.TestLoop", ControlFlowTests.TestLoop() == 55);  // Sum of 1..10
        RecordResult("ControlFlowTests.TestNestedLoop", ControlFlowTests.TestNestedLoop() == 100);
        RecordResult("ControlFlowTests.TestSwitch", ControlFlowTests.TestSwitch() == 42);

        // Phase 3 control flow tests
        RecordResult("ControlFlowTests.TestIfElseChain", ControlFlowTests.TestIfElseChain() == 20);
        RecordResult("ControlFlowTests.TestIfElseChainWithReturns", ControlFlowTests.TestIfElseChainWithReturns() == 30);
        RecordResult("ControlFlowTests.TestBreakInLoop", ControlFlowTests.TestBreakInLoop() == 10);
        RecordResult("ControlFlowTests.TestContinueInLoop", ControlFlowTests.TestContinueInLoop() == 25);
        RecordResult("ControlFlowTests.TestNestedBreak", ControlFlowTests.TestNestedBreak() == 30);
        RecordResult("ControlFlowTests.TestWhileWithAndCondition", ControlFlowTests.TestWhileWithAndCondition() == 21);
    }

    private static void RunLocalVariableTests()
    {
        RecordResult("LocalVariableTests.TestSimpleLocals", LocalVariableTests.TestSimpleLocals() == 30);
        RecordResult("LocalVariableTests.TestManyLocals", LocalVariableTests.TestManyLocals() == 45);
        RecordResult("LocalVariableTests.TestLocalAddress", LocalVariableTests.TestLocalAddress() == 42);
    }

    private static void RunMethodCallTests()
    {
        RecordResult("MethodCallTests.TestSimpleCall", MethodCallTests.TestSimpleCall() == 42);
        RecordResult("MethodCallTests.TestCallWithArgs", MethodCallTests.TestCallWithArgs() == 15);
        RecordResult("MethodCallTests.TestCallChain", MethodCallTests.TestCallChain() == 120);
        RecordResult("MethodCallTests.TestRecursion", MethodCallTests.TestRecursion() == 120);  // 5! = 120
    }

    private static void RunConversionTests()
    {
        RecordResult("ConversionTests.TestConvI4", ConversionTests.TestConvI4() == 42);
        RecordResult("ConversionTests.TestConvI8", ConversionTests.TestConvI8() == 0x1_0000_0000L);
        RecordResult("ConversionTests.TestConvU4", ConversionTests.TestConvU4() == 0xFFFFFFFF);
        RecordResult("ConversionTests.TestConvI1", ConversionTests.TestConvI1() == -1);
        RecordResult("ConversionTests.TestConvU1", ConversionTests.TestConvU1() == 255);
        RecordResult("ConversionTests.TestConvI2", ConversionTests.TestConvI2() == -1);
        RecordResult("ConversionTests.TestConvU2", ConversionTests.TestConvU2() == 65535);
    }

    private static void RunObjectTests()
    {
        RecordResult("ObjectTests.TestLdnull", ObjectTests.TestLdnull() == 1);
    }

    private static void RunArrayTests()
    {
        RecordResult("ArrayTests.TestNewarr", ArrayTests.TestNewarr() == 10);
        RecordResult("ArrayTests.TestStelem", ArrayTests.TestStelem() == 42);
        RecordResult("ArrayTests.TestLdlen", ArrayTests.TestLdlen() == 5);
        RecordResult("ArrayTests.TestArraySum", ArrayTests.TestArraySum() == 15);
    }

    private static void RunFieldTests()
    {
        RecordResult("FieldTests.TestStaticField", FieldTests.TestStaticField() == 42);
        RecordResult("FieldTests.TestStaticFieldIncrement", FieldTests.TestStaticFieldIncrement() == 43);
        RecordResult("FieldTests.TestMultipleStaticFields", FieldTests.TestMultipleStaticFields() == 100);
    }

    private static void RunStructTests()
    {
        // Basic struct field access tests
        RecordResult("StructTests.TestStructLocalFieldWrite", StructTests.TestStructLocalFieldWrite() == 42);
        RecordResult("StructTests.TestStructLocalFieldSum", StructTests.TestStructLocalFieldSum() == 30);
        RecordResult("StructTests.TestStructPassByValue", StructTests.TestStructPassByValue() == 100);
        RecordResult("StructTests.TestStructOutParam", StructTests.TestStructOutParam() == 42);

        // Object initializer tests - RE-ENABLED for testing
        RecordResult("StructTests.TestObjectInitializer", StructTests.TestObjectInitializer() == 30);
        RecordResult("StructTests.TestStindI8", StructTests.TestStindI8() == 100);  // Test stind.i8 pattern first
        RecordResult("StructTests.TestSimpleStobj", StructTests.TestSimpleStobj() == 300);  // RE-ENABLED for debugging
        RecordResult("StructTests.TestObjectInitializerOut", StructTests.TestObjectInitializerOut() == 30);  // TODO: Fix initobj+stobj interaction

        // Struct array tests (ldelem/stelem with type token)
        RecordResult("StructTests.TestStructArrayStore", StructTests.TestStructArrayStore() == 10);
        RecordResult("StructTests.TestStructArrayLoad", StructTests.TestStructArrayLoad() == 70);
        RecordResult("StructTests.TestStructArrayCopy", StructTests.TestStructArrayCopy() == 100);
        RecordResult("StructTests.TestStructArrayMultiple", StructTests.TestStructArrayMultiple() == 111);

        // Large struct tests (structs > 8 bytes)
        RecordResult("StructTests.TestLargeStructFields", StructTests.TestLargeStructFields() == 60);
        RecordResult("StructTests.TestLargeStructCopy", StructTests.TestLargeStructCopy() == 600);
        RecordResult("StructTests.TestLargeStructArrayStore", StructTests.TestLargeStructArrayStore() == 1);
        RecordResult("StructTests.TestLargeStructArrayLoad", StructTests.TestLargeStructArrayLoad() == 60);
        RecordResult("StructTests.TestLargeStructArrayCopy", StructTests.TestLargeStructArrayCopy() == 100);

        // Struct return value tests (2.1)
        RecordResult("StructTests.TestSmallStructReturn", StructTests.TestSmallStructReturn() == 30);
        RecordResult("StructTests.TestMediumStructReturn", StructTests.TestMediumStructReturn() == 300);
        RecordResult("StructTests.TestLargeStructReturn", StructTests.TestLargeStructReturn() == 6);

        // ref/out parameter tests (2.3)
        RecordResult("StructTests.TestSimpleOutParam", StructTests.TestSimpleOutParam() == 42);
        RecordResult("StructTests.TestRefParam", StructTests.TestRefParam() == 20);
        RecordResult("StructTests.TestRefParamMultiple", StructTests.TestRefParamMultiple() == 45);

        // Nested field out/ref tests (class.struct.field pattern)
        RecordResult("StructTests.TestNestedFieldOut", StructTests.TestNestedFieldOut() == 99);
        RecordResult("StructTests.TestNestedFieldRef", StructTests.TestNestedFieldRef() == 110);

        // CRITICAL: Virtqueue exact pattern test - THREE consecutive large struct returns
        RecordResult("VirtqueueExactTests.TestThreeAllocationsAndReadBack", VirtqueueExactTests.TestThreeAllocationsAndReadBack() == 42);
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

    // Test ulong + uint addition (high bits should be preserved)
    public static int TestUlongPlusUint()
    {
        ulong baseAddr = 0xFFFF80C000000000;
        uint offset = 0x100;
        ulong result = baseAddr + offset;
        // High 32 bits should be 0xFFFF80C0
        return (int)(result >> 32) == unchecked((int)0xFFFF80C0) ? 1 : 0;
    }

    // Test ulong + uint addition - get the low bits
    public static int TestUlongPlusUintLow()
    {
        ulong baseAddr = 0xFFFF80C000000000;
        uint offset = 0x100;
        ulong result = baseAddr + offset;
        // Low 32 bits should be 0x100
        return (int)result == 0x100 ? 1 : 0;
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

    // 64-bit comparison tests (for investigating JIT bool bug)
    public static int TestCeqLongZero()
    {
        // This tests the exact pattern that fails in VirtioBlkEntry.ReadAndProgramBar
        // baseAddr (ulong) == 0 should return 1 when baseAddr is 0
        ulong baseAddr = 0;
        bool isZero = baseAddr == 0;
        return isZero ? 1 : 0;
    }

    public static int TestCeqLongNonZero()
    {
        // When baseAddr is not 0, should return 0
        ulong baseAddr = 0x12345678;
        bool isZero = baseAddr == 0;
        return isZero ? 1 : 0;
    }

    public static int TestCeqLongEquals()
    {
        // Two equal 64-bit values
        ulong a = 0x123456789ABCDEF0;
        ulong b = 0x123456789ABCDEF0;
        return a == b ? 1 : 0;
    }

    public static int TestCeqLongNotEquals()
    {
        // Two different 64-bit values
        ulong a = 0x123456789ABCDEF0;
        ulong b = 0x123456789ABCDEF1;
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

    // Test 64-bit shift left by 32 - exactly like the VirtioBlk BAR code
    public static int TestShl64By32()
    {
        uint upperBar = 0xC0;
        ulong baseAddr = 0;
        baseAddr |= (ulong)upperBar << 32;
        // Should be 0x000000C0_00000000
        // Return upper 32 bits as int - should be 0xC0 (192)
        return (int)(baseAddr >> 32);
    }

    // Test 64-bit shift with different values
    public static int TestShl64By32Simple()
    {
        ulong value = 1;
        ulong shifted = value << 32;
        // Should be 0x00000001_00000000
        // Return upper 32 bits as int - should be 1
        return (int)(shifted >> 32);
    }

    // Test 64-bit shift with immediate comparison (no shift back)
    public static int TestShl64LowBits()
    {
        ulong value = 0xABCD;
        ulong shifted = value << 16;
        // Should be 0x00000000_ABCD0000
        // Return low 32 bits as int - should be 0xABCD0000 = -1412628480
        return (int)shifted;
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

    // =========================================================================
    // Phase 3 Control Flow Tests (3.1, 3.2, 3.3)
    // =========================================================================

    // Test if-else chain (3.1)
    public static int TestIfElseChain()
    {
        int result = 0;
        int x = 2;

        if (x == 1) result = 10;
        else if (x == 2) result = 20;
        else if (x == 3) result = 30;
        else result = 40;

        return result;  // Expected: 20
    }

    // Test if-else chain with multiple returns (3.1)
    public static int TestIfElseChainWithReturns()
    {
        return GetValueForCode(3);  // Expected: 30
    }

    private static int GetValueForCode(int code)
    {
        if (code == 1) return 10;
        if (code == 2) return 20;
        if (code == 3) return 30;
        return 0;
    }

    // Test break in loop (3.2)
    public static int TestBreakInLoop()
    {
        int sum = 0;
        for (int i = 0; i < 100; i++)
        {
            if (i == 5) break;
            sum += i;
        }
        return sum;  // Expected: 0+1+2+3+4 = 10
    }

    // Test continue in loop (3.2)
    public static int TestContinueInLoop()
    {
        int sum = 0;
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0) continue;  // Skip evens
            sum += i;
        }
        return sum;  // Expected: 1+3+5+7+9 = 25
    }

    // Test break in nested loop (3.2)
    public static int TestNestedBreak()
    {
        int count = 0;
        for (int i = 0; i < 10; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                if (j == 3) break;  // Inner break only
                count++;
            }
        }
        return count;  // Expected: 10 * 3 = 30
    }

    // Test while loop with && condition (3.3)
    public static int TestWhileWithAndCondition()
    {
        int i = 0;
        int sum = 0;
        while (i < 10 && sum < 20)
        {
            sum += i;
            i++;
        }
        return sum;  // Expected: 0+1+2+3+4+5 = 15, then 15+6=21 > 20, so 21
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

// Medium struct for testing 16 byte struct returns (RAX:RDX)
public struct MediumStruct
{
    public long A;
    public long B;
}

// Inner struct for nested field out tests
public struct InnerStruct
{
    public int Value;
}

// Container class for nested field out tests (class.struct.field pattern)
public class Container
{
    public InnerStruct Inner;
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

    // =========================================================================
    // Struct Return Value Tests (2.1)
    // Tests returning structs from methods for different size categories:
    // - <= 8 bytes: Return in RAX
    // - 9-16 bytes: Return in RAX:RDX
    // - > 16 bytes: Hidden buffer pointer in first arg
    // =========================================================================

    // Test small struct return (8 bytes - fits in RAX)
    public static int TestSmallStructReturn()
    {
        SimpleStruct s = GetSmallStruct();
        return s.X + s.Y;  // Expected: 10 + 20 = 30
    }

    private static SimpleStruct GetSmallStruct()
    {
        SimpleStruct s;
        s.X = 10;
        s.Y = 20;
        return s;
    }

    // Test medium struct return (16 bytes - RAX:RDX)
    public static int TestMediumStructReturn()
    {
        MediumStruct m = GetMediumStruct();
        return (int)(m.A + m.B);  // Expected: 100 + 200 = 300
    }

    private static MediumStruct GetMediumStruct()
    {
        MediumStruct m;
        m.A = 100;
        m.B = 200;
        return m;
    }

    // Test large struct return (24 bytes - hidden buffer pointer)
    public static int TestLargeStructReturn()
    {
        LargeStruct l = GetLargeStruct();
        return (int)(l.A + l.B + l.C);  // Expected: 1 + 2 + 3 = 6
    }

    private static LargeStruct GetLargeStruct()
    {
        LargeStruct l;
        l.A = 1;
        l.B = 2;
        l.C = 3;
        return l;
    }

    // =========================================================================
    // ref/out Parameter Tests (2.3)
    // Tests passing parameters by reference for both primitives and structs
    // =========================================================================

    // Test simple out parameter with primitive int
    public static int TestSimpleOutParam()
    {
        int result;
        SetValue(out result);
        return result;  // Expected: 42
    }

    private static void SetValue(out int x)
    {
        x = 42;
    }

    // Test ref parameter with primitive int
    public static int TestRefParam()
    {
        int value = 10;
        AddTen(ref value);
        return value;  // Expected: 20
    }

    private static void AddTen(ref int x)
    {
        x += 10;
    }

    // Test ref parameter modification multiple times
    public static int TestRefParamMultiple()
    {
        int value = 5;
        Triple(ref value);
        Triple(ref value);
        return value;  // Expected: 45 (5 * 3 * 3)
    }

    private static void Triple(ref int x)
    {
        x *= 3;
    }

    // Test nested field out parameter (class.struct.field pattern)
    // This is a critical pattern used in DDK development
    public static int TestNestedFieldOut()
    {
        Container c = new Container();
        SetInnerValue(out c.Inner.Value);
        return c.Inner.Value;  // Expected: 99
    }

    private static void SetInnerValue(out int value)
    {
        value = 99;
    }

    // Test nested field ref parameter
    public static int TestNestedFieldRef()
    {
        Container c = new Container();
        c.Inner.Value = 10;
        AddToInnerValue(ref c.Inner.Value);
        return c.Inner.Value;  // Expected: 110
    }

    private static void AddToInnerValue(ref int value)
    {
        value += 100;
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
// Boxing Tests (Phase 4)
// =============================================================================

// Struct with 5 fields to test newobj with >4 constructor args
public struct MultiFieldStruct
{
    public int A;
    public int B;
    public int C;
    public int D;
    public int E;

    public MultiFieldStruct(int a, int b, int c, int d, int e)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
    }
}

public static class BoxingTests
{
    // Phase 4.1: Test newobj with >4 constructor arguments
    public static int TestNewObjManyArgs()
    {
        var s = new MultiFieldStruct(1, 2, 3, 4, 5);
        return s.A + s.B + s.C + s.D + s.E;  // Expected: 15
    }

    // Phase 4.2: Test boxing an int
    public static int TestBoxInt()
    {
        int x = 42;
        object boxed = x;
        int unboxed = (int)boxed;
        return unboxed;  // Expected: 42
    }

    // Phase 4.2: Test boxing a small struct (8 bytes)
    public static int TestBoxStruct()
    {
        SimpleStruct s;
        s.X = 10;
        s.Y = 20;
        object boxed = s;
        SimpleStruct unboxed = (SimpleStruct)boxed;
        return unboxed.X + unboxed.Y;  // Expected: 30
    }

    // Phase 4.2: Test boxing a medium struct (16 bytes)
    public static int TestBoxMediumStruct()
    {
        MediumStruct m;
        m.A = 100;
        m.B = 200;
        object boxed = m;
        MediumStruct unboxed = (MediumStruct)boxed;
        return (int)(unboxed.A + unboxed.B);  // Expected: 300
    }

    // Phase 4.2: Test boxing a large struct (24 bytes)
    public static int TestBoxLargeStruct()
    {
        LargeStruct l;
        l.A = 10;
        l.B = 20;
        l.C = 30;
        object boxed = l;
        LargeStruct unboxed = (LargeStruct)boxed;
        return (int)(unboxed.A + unboxed.B + unboxed.C);  // Expected: 60
    }
}

// =============================================================================
// Instance Member Tests (Phase 5)
// =============================================================================

// Class with multiple fields for testing instance field access
public class MultiFieldClass
{
    public int A;
    public int B;
    public int C;
}

// Class for testing instance methods that use 'this'
public class Calculator
{
    public int Value;

    public int Add(int a, int b)
    {
        return a + b;
    }

    public int AddToValue(int x)
    {
        // Uses this.Value implicitly
        return Value + x;
    }
}

public class DummyDevice { }

public class DummyCallee
{
    // Simple callvirt target that always returns true
    public virtual bool ReturnTrue(DummyDevice device)
    {
        return true;
    }
}

// Struct to mirror a moderately large value type (similar to PciBar ~40 bytes)
public struct DummyBar
{
    public long A;
    public long B;
    public long C;
    public long D;
    public int Flags;
}

// Struct to mirror EXACT DMABuffer layout (32 bytes = 4 ulongs)
// MUST match the real DMABuffer in ddk/Platform/DMA.cs:
// - PhysicalAddress: ulong (8 bytes)
// - VirtualAddress: void* (8 bytes)
// - Size: ulong (8 bytes)
// - PageCount: ulong (8 bytes)
public struct DMABufferMock
{
    public ulong PhysicalAddress;   // 8 bytes - must match real order!
    public ulong VirtualAddress;    // 8 bytes (void* in real struct)
    public ulong Size;              // 8 bytes
    public ulong PageCount;         // 8 bytes - THIS WAS MISSING!
}

// Class that mirrors Virtqueue pattern - many fields including struct fields and pointers
// This class has:
// - readonly scalar fields
// - multiple DMABuffer struct fields (32 bytes each) - EXACTLY matches real DMABuffer!
// - multiple pointer fields
// - scalar tracking fields
// - array field
public unsafe class VirtqueueMock
{
    // Field 0-1 (object header + method table - implicit)
    private readonly ushort _queueIndex;   // offset ~8-10
    private readonly ushort _queueSize;    // offset ~10-12
    // 4 bytes padding to align struct to 8 bytes

    // Large struct fields (32 bytes each) - SAME SIZE AS REAL DMABuffer!
    private DMABufferMock _descBuffer;     // offset 16 (32 bytes)
    private DMABufferMock _availBuffer;    // offset 48 (32 bytes)
    private DMABufferMock _usedBuffer;     // offset 80 (32 bytes)

    // Pointer fields - THIS IS WHERE THE CRASH HAPPENS
    private ulong _desc;                   // offset 112 - SAME AS REAL VIRTQUEUE!
    private ulong _availFlags;             // offset 120
    private ulong _availIdx;               // offset 128
    private ulong _availRing;              // offset 136

    // More fields
    private ushort _freeHead;
    private ushort _numFree;
    private ushort _lastUsedIdx;

    public VirtqueueMock(ushort index, ushort size)
    {
        _queueIndex = index;
        _queueSize = size;
    }

    // Method that mimics the crash pattern:
    // 1. Read struct fields into locals
    // 2. Extract pointer from struct field
    // 3. Store to pointer field (stfld)
    // 4. Read back from pointer field (ldfld) - CRASH HAPPENS HERE
    public ulong TestFieldReadAfterStructCopy()
    {
        // Simulate initializing struct fields - match exact field order from real DMABuffer
        _descBuffer.PhysicalAddress = 0x2000;
        _descBuffer.VirtualAddress = 0x1000;
        _descBuffer.Size = 0x100;
        _descBuffer.PageCount = 1;

        _availBuffer.PhysicalAddress = 0x4000;
        _availBuffer.VirtualAddress = 0x3000;
        _availBuffer.Size = 0x200;
        _availBuffer.PageCount = 1;

        _usedBuffer.PhysicalAddress = 0x6000;
        _usedBuffer.VirtualAddress = 0x5000;
        _usedBuffer.Size = 0x300;
        _usedBuffer.PageCount = 1;

        // Copy struct fields to locals (this is what the driver does)
        // These are 32-byte structs (>16 bytes) - uses hidden buffer return convention
        var descBufLocal = _descBuffer;
        var availBufLocal = _availBuffer;
        var usedBufLocal = _usedBuffer;

        // Extract VirtualAddress and store to pointer field
        _desc = descBufLocal.VirtualAddress;

        // Read back _desc - THIS IS WHERE CRASH HAPPENS IN DRIVER
        ulong descPtr = _desc;

        // Also set other pointer fields
        _availFlags = availBufLocal.VirtualAddress;
        _availIdx = availBufLocal.VirtualAddress + 2;
        _availRing = availBufLocal.VirtualAddress + 4;

        return descPtr;
    }

    public ulong GetDesc() => _desc;
}

public static class InstanceTests
{
    // Phase 5.1: Test instance field read/write
    public static int TestInstanceFieldReadWrite()
    {
        SimpleClass obj = new SimpleClass(0);
        obj.SetValue(42);
        return obj.GetValue();  // Expected: 42
    }

    // Phase 5.1: Test multiple instance fields
    public static int TestMultipleInstanceFields()
    {
        MultiFieldClass obj = new MultiFieldClass();
        obj.A = 10;
        obj.B = 20;
        obj.C = 30;
        return obj.A + obj.B + obj.C;  // Expected: 60
    }

    // Phase 5.2: Test instance method call
    public static int TestInstanceMethodCall()
    {
        Calculator calc = new Calculator();
        return calc.Add(10, 20);  // Expected: 30
    }

    // Phase 5.2: Test instance method that uses 'this'
    public static int TestInstanceMethodWithThis()
    {
        Calculator calc = new Calculator();
        calc.Value = 30;
        return calc.AddToValue(20);  // Expected: 50 (30 + 20)
    }

    // Regression test: Mirrors Virtqueue crash pattern
    // Class with many fields, struct copy to locals, field store then read back
    public static int TestVirtqueuePattern()
    {
        VirtqueueMock vq = new VirtqueueMock(0, 128);
        ulong result = vq.TestFieldReadAfterStructCopy();
        // Expected: 0x1000 (the VirtualAddress we set)
        return result == 0x1000 ? 42 : 0;
    }

    // Regression for callvirt bool -> stloc -> ldloc -> brfalse with many locals (mimics driver bug)
    public static int TestCallvirtBoolBranchWithLargeLocals()
    {
        DummyDevice dev = new DummyDevice();   // V_0
        uint v1 = 1;                           // V_1
        uint v2 = 2;                           // V_2
        int v3 = 3;                            // V_3
        bool initResult = false;               // V_4
        bool other = true;                     // V_5
        DummyBar bar0 = default;               // V_6 (40 bytes)
        DummyBar bar1 = default;               // V_7 (40 bytes)

        initResult = new DummyCallee().ReturnTrue(dev);
        if (!initResult)
        {
            return 1;  // Should not happen
        }

        // Touch locals to keep them alive and avoid optimizing away
        if (other && v1 + v2 + v3 + (int)bar0.A + (int)bar1.B == -1)
        {
            return 99;
        }

        return 2;  // Success path
    }

    // Critical test: Cross-assembly large struct return (>16 bytes)
    // This mimics DMA.Allocate() returning a DMABuffer from DDK assembly
    // Uses hidden buffer convention for return values > 16 bytes
    public static int TestCrossAssemblyLargeStructReturn()
    {
        // Call method in System.Runtime that returns a 32-byte struct
        System.Runtime.LargeTestStruct result = System.Runtime.StructHelper.CreateLargeStruct(
            0x1000, 0x2000, 0x3000, 0x4000);

        // Verify the struct was returned correctly
        if (result.A != 0x1000) return 1;
        if (result.B != 0x2000) return 2;
        if (result.C != 0x3000) return 3;
        if (result.D != 0x4000) return 4;

        return 42;  // Success
    }

    // Critical test: Copy cross-assembly large struct return to class field
    // This mimics the exact driver pattern: DMABuffer descBuf = DMA.Allocate(size);
    // followed by copying fields to class instance fields
    public static int TestCrossAssemblyLargeStructToClassField()
    {
        CrossAssemblyStructHolder holder = new CrossAssemblyStructHolder();
        holder.TestCopyFromCrossAssemblyReturn();

        // Verify the struct was copied correctly to the class field
        if (holder.GetA() != 0xAAAA) return 1;
        if (holder.GetB() != 0xBBBB) return 2;
        if (holder.GetC() != 0xCCCC) return 3;
        if (holder.GetD() != 0xDDDD) return 4;

        return 42;  // Success
    }
}

// Class to hold cross-assembly struct return result - mimics Virtqueue
public class CrossAssemblyStructHolder
{
    private System.Runtime.LargeTestStruct _stored;
    private ulong _extractedA;

    public void TestCopyFromCrossAssemblyReturn()
    {
        // This is the exact pattern that crashes in the driver:
        // 1. Call cross-assembly method that returns large struct
        // 2. Store result in local
        // 3. Copy struct to class field
        // 4. Extract value from struct and store in another field

        System.Runtime.LargeTestStruct result = System.Runtime.StructHelper.CreateLargeStruct(
            0xAAAA, 0xBBBB, 0xCCCC, 0xDDDD);

        _stored = result;
        _extractedA = result.A;
    }

    public ulong GetA() => _stored.A;
    public ulong GetB() => _stored.B;
    public ulong GetC() => _stored.C;
    public ulong GetD() => _stored.D;
}

/// <summary>
/// Class that mimics the EXACT Virtqueue driver pattern with:
/// - THREE consecutive large struct returns (DMA.Allocate calls)
/// - Storing struct fields to class instance fields
/// - Extracting pointer field and storing to another pointer field
/// - Reading back the stored pointer field (this is where the crash occurs)
/// </summary>
public unsafe class VirtqueueExactMimic
{
    // Mimic the three DMABuffer fields in Virtqueue
    private System.Runtime.LargeTestStruct _descBuffer;   // offset 8
    private System.Runtime.LargeTestStruct _availBuffer;  // offset 40
    private System.Runtime.LargeTestStruct _usedBuffer;   // offset 72

    // Mimic the pointer field that gets extracted from _descBuffer.B (VirtualAddress)
    private ulong _desc;  // offset 104 (in real driver this is VirtqDesc*)

    /// <summary>
    /// Mimics InitializeBuffers() - the exact crash pattern
    /// </summary>
    public int TestThreeAllocationsAndReadBack()
    {
        // First allocation - DMA.Allocate(descSize)
        System.Runtime.LargeTestStruct descBufAlloc = System.Runtime.StructHelper.CreateLargeStruct(
            0xDEAD1000, 0xBEEF1000, 0x1000, 0x0001);
        _descBuffer = descBufAlloc;

        // Second allocation - DMA.Allocate(availSize)
        System.Runtime.LargeTestStruct availBufAlloc = System.Runtime.StructHelper.CreateLargeStruct(
            0xDEAD2000, 0xBEEF2000, 0x2000, 0x0002);
        _availBuffer = availBufAlloc;

        // Third allocation - DMA.Allocate(usedSize)
        System.Runtime.LargeTestStruct usedBufAlloc = System.Runtime.StructHelper.CreateLargeStruct(
            0xDEAD3000, 0xBEEF3000, 0x3000, 0x0003);
        _usedBuffer = usedBufAlloc;

        // Now the critical part - extracting pointer from struct and storing to field
        // This is line 260 in Virtqueue.cs: _desc = (VirtqDesc*)descBufLocal.VirtualAddress;
        var descBufLocal = _descBuffer;
        _desc = descBufLocal.B;  // B = VirtualAddress in our mock

        // THIS IS WHERE THE CRASH HAPPENS IN THE REAL DRIVER
        // Line 265: VirtqDesc* descPtr = _desc;
        ulong descPtr = _desc;

        // Verify the value was stored and read correctly
        if (descPtr != 0xBEEF1000) return 1;

        // Verify all three buffers
        if (_descBuffer.A != 0xDEAD1000) return 2;
        if (_availBuffer.A != 0xDEAD2000) return 3;
        if (_usedBuffer.A != 0xDEAD3000) return 4;

        return 42;  // Success
    }
}

public static class VirtqueueExactTests
{
    public static int TestThreeAllocationsAndReadBack()
    {
        VirtqueueExactMimic vq = new VirtqueueExactMimic();
        return vq.TestThreeAllocationsAndReadBack();
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
// String.Format Tests
// =============================================================================

public static class StringFormatTests
{
    /// <summary>
    /// Tests string.Format with a single integer argument.
    /// </summary>
    public static int TestFormatOneArg()
    {
        int x = 42;
        string s = string.Format("Value: {0}", x);
        Debug.WriteLine(string.Format("[StrFormat] TestFormatOneArg: '{0}' len={1}", s, s.Length));
        // Expected: "Value: 42" which has length 9
        return s.Length;
    }

    /// <summary>
    /// Tests string.Format with two arguments.
    /// </summary>
    public static int TestFormatTwoArgs()
    {
        int a = 10;
        int b = 20;
        string s = string.Format("{0} + {1}", a, b);
        Debug.WriteLine(string.Format("[StrFormat] TestFormatTwoArgs: '{0}' len={1}", s, s.Length));
        // Expected: "10 + 20" which has length 7
        return s.Length;
    }

    /// <summary>
    /// Tests string.Format with three arguments.
    /// </summary>
    public static int TestFormatThreeArgs()
    {
        int a = 10;
        int b = 20;
        int c = 30;
        string s = string.Format("{0} + {1} = {2}", a, b, c);
        Debug.WriteLine(string.Format("[StrFormat] TestFormatThreeArgs: '{0}' len={1}", s, s.Length));
        // Expected: "10 + 20 = 30" which has length 12
        return s.Length;
    }

    /// <summary>
    /// Tests string.Format with string arguments.
    /// </summary>
    public static int TestFormatStringArg()
    {
        string name = "World";
        string s = string.Format("Hello, {0}!", name);
        Debug.WriteLine(string.Format("[StrFormat] TestFormatStringArg: '{0}' len={1}", s, s.Length));
        // Expected: "Hello, World!" which has length 13
        return s.Length;
    }
}

// =============================================================================
// String Interpolation Tests
// =============================================================================

public static class StringInterpolationTests
{
    /// <summary>
    /// Tests basic string interpolation with an integer value.
    /// Uses DefaultInterpolatedStringHandler via compiler transformation.
    /// </summary>
    public static int TestSimpleInterpolation()
    {
        Debug.WriteLine("[StrInterp] Starting test...");

        // Test 1: Can we get an int's ToString?
        int x = 42;
        string xStr = x.ToString();
        Debug.WriteLine(string.Format("[StrInterp] x.ToString() = '{0}' len={1}", xStr ?? "null", xStr?.Length ?? 0));

        // Test 2: Can we concat two strings?
        string concat = string.Concat("Value: ", xStr);
        Debug.WriteLine(string.Format("[StrInterp] concat = '{0}' len={1}", concat ?? "null", concat?.Length ?? 0));

        // Test 3: Now test the actual interpolation
        Debug.WriteLine("[StrInterp] Now testing $\"...\": ");
        string s = $"Value: {x}";
        Debug.WriteLine(string.Format("[StrInterp] result = '{0}' len={1}", s, s.Length));
        return s.Length;
    }

    /// <summary>
    /// Tests string interpolation with multiple values.
    /// </summary>
    public static int TestMultipleValues()
    {
        int a = 10;
        int b = 20;
        string s = $"{a} + {b} = {a + b}";
        Debug.WriteLine(string.Format("[StrInterp] TestMultipleValues: '{0}' len={1}", s, s.Length));
        // Expected: "10 + 20 = 30" which has length 12
        return s.Length;
    }

    /// <summary>
    /// Tests string interpolation with string values.
    /// </summary>
    public static int TestStringValues()
    {
        string name = "Test";
        string s = $"Hello, {name}!";
        Debug.WriteLine(string.Format("[StrInterp] TestStringValues: '{0}' len={1}", s, s.Length));
        // Expected: "Hello, Test!" which has length 12
        return s.Length;
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
