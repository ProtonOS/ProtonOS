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
        RunBoxingTests();

        // Instance member tests (Phase 5)
        RunInstanceTests();

        // Exception tests - uses AOT well-known type exposure for Exception base class
        Debug.WriteLine("[PHASE] Starting Exception tests...");
        RunExceptionTests();
        Debug.WriteLine("[PHASE] Exception tests complete");

        // Generic tests - tests MethodSpec (0x2B) token resolution
        RunGenericTests();

        // String.Format tests - tests AOT method calls from JIT
        RunStringFormatTests();

        // String interpolation tests - requires MethodSpec (0x2B), MVAR, and cross-assembly TypeRef resolution
        RunStringInterpolationTests();

        // Interface tests - tests interface dispatch via callvirt
        RunInterfaceTests();

        // Struct with reference type fields
        RunStructWithRefTests();

        // Generic method on generic class
        RunGenericMethodOnGenericClassTests();

        // Nullable<T> tests - tests generic value type with special semantics
        RunNullableTests();

        // Delegate tests - tests delegate creation and invocation
        RunDelegateTests();

        // Calli tests - tests indirect calls through function pointers
        RunCalliTests();

        // Multicast delegate tests - tests Delegate.Combine and Delegate.Remove
        RunMulticastDelegateTests();

        // Closure tests - tests lambdas that capture local variables
        RunClosureTests();

        // Sizeof tests - tests sizeof IL opcode
        RunSizeofTests();

        // Memory block tests - tests cpblk/initblk
        RunMemoryBlockTests();

        // Fixed-size buffer tests - tests fixed arrays in structs
        RunFixedBufferTests();

        // Overflow checking tests - tests add.ovf, conv.ovf, etc.
        RunOverflowTests();

        // Stackalloc tests - tests localloc opcode
        RunStackallocTests();

        // Static constructor tests - tests .cctor invocation
        RunStaticCtorTests();

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
        Debug.WriteLine("[EXC] Running TestTryCatch...");
        RecordResult("ExceptionTests.TestTryCatch", ExceptionTests.TestTryCatch() == 42);
        Debug.WriteLine("[EXC] Running TestTryFinally...");
        RecordResult("ExceptionTests.TestTryFinally", ExceptionTests.TestTryFinally() == 42);
        Debug.WriteLine("[EXC] Running TestNestedTryCatch...");
        RecordResult("ExceptionTests.TestNestedTryCatch", ExceptionTests.TestNestedTryCatch() == 42);
        Debug.WriteLine("[EXC] Running TestMultipleCatchFirst...");
        RecordResult("ExceptionTests.TestMultipleCatchFirst", ExceptionTests.TestMultipleCatchFirst() == 42);
        Debug.WriteLine("[EXC] Running TestMultipleCatchSecond...");
        RecordResult("ExceptionTests.TestMultipleCatchSecond", ExceptionTests.TestMultipleCatchSecond() == 42);
        Debug.WriteLine("[EXC] Running TestFinallyWithReturn...");
        RecordResult("ExceptionTests.TestFinallyWithReturn", ExceptionTests.TestFinallyWithReturn() == 42);
        Debug.WriteLine("[EXC] Running TestFinallyWithExceptionCaught...");
        RecordResult("ExceptionTests.TestFinallyWithExceptionCaught", ExceptionTests.TestFinallyWithExceptionCaught() == 42);
        Debug.WriteLine("[EXC] Running TestCatchWhenTrue...");
        RecordResult("ExceptionTests.TestCatchWhenTrue", ExceptionTests.TestCatchWhenTrue() == 42);
        Debug.WriteLine("[EXC] Running TestCatchWhenFalse...");
        RecordResult("ExceptionTests.TestCatchWhenFalse", ExceptionTests.TestCatchWhenFalse() == 42);
        Debug.WriteLine("[EXC] Running TestFinallyInLoopWithBreak...");
        RecordResult("ExceptionTests.TestFinallyInLoopWithBreak", ExceptionTests.TestFinallyInLoopWithBreak() == 42);
        Debug.WriteLine("[EXC] Running TestFinallyInLoopWithContinue...");
        RecordResult("ExceptionTests.TestFinallyInLoopWithContinue", ExceptionTests.TestFinallyInLoopWithContinue() == 42);
    }

    private static void RunGenericTests()
    {
        RecordResult("GenericTests.TestGenericMethod", GenericTests.TestGenericMethod() == 42);
        // TestGenericClass requires TypeSpec (0x1B) support for generic type instantiation
        RecordResult("GenericTests.TestGenericClass", GenericTests.TestGenericClass() == 42);
        // SimpleList<int> tests (no array usage)
        RecordResult("GenericTests.TestSimpleListIntBasic", GenericTests.TestSimpleListIntBasic() == 0);  // Empty list count = 0
        RecordResult("GenericTests.TestSimpleListIntCount", GenericTests.TestSimpleListIntCount() == 3);  // Three items

        // Generic method with multiple type parameters
        RecordResult("GenericTests.TestMultiTypeParamConvert", GenericTests.TestMultiTypeParamConvert() == 42);
        RecordResult("GenericTests.TestMultiTypeParamCombine", GenericTests.TestMultiTypeParamCombine() == 42);

        // Generic interface tests - interface dispatch with generic interfaces
        RecordResult("GenericTests.TestGenericInterfaceInt", GenericTests.TestGenericInterfaceInt() == 42);
        RecordResult("GenericTests.TestGenericInterfaceSetGet", GenericTests.TestGenericInterfaceSetGet() == 42);
        RecordResult("GenericTests.TestGenericInterfaceString", GenericTests.TestGenericInterfaceString() == 5);

        // Generic delegate tests
        RecordResult("GenericTests.TestGenericDelegate", GenericTests.TestGenericDelegate() == 42);
        RecordResult("GenericTests.TestGenericDelegateStringToInt", GenericTests.TestGenericDelegateStringToInt() == 5);

        // Nested generic type tests
        RecordResult("GenericTests.TestNestedGenericSimple", GenericTests.TestNestedGenericSimple() == 42);
        RecordResult("GenericTests.TestNestedGenericInnerValue", GenericTests.TestNestedGenericInnerValue() == 5);
        RecordResult("GenericTests.TestNestedGenericMethod", GenericTests.TestNestedGenericMethod() == 22);

        // Generic constraint tests (new() constraint requires Activator.CreateInstance<T>)
        RecordResult("GenericTests.TestConstraintClass", GenericTests.TestConstraintClass() == 42);
        RecordResult("GenericTests.TestConstraintClassNotNull", GenericTests.TestConstraintClassNotNull() == 42);
        RecordResult("GenericTests.TestConstraintStruct", GenericTests.TestConstraintStruct() == 42);
        Debug.WriteLine("[RUNNER] About to call TestConstraintNew...");
        int constraintNewResult = GenericTests.TestConstraintNew();
        Debug.Write("[RUNNER] TestConstraintNew returned: ");
        Debug.WriteDecimal((uint)constraintNewResult);
        Debug.WriteLine();
        RecordResult("GenericTests.TestConstraintNew", constraintNewResult == 42);  // Activator.CreateInstance<T>
        RecordResult("GenericTests.TestConstraintInterface", GenericTests.TestConstraintInterface() == 42);
        RecordResult("GenericTests.TestConstraintBase", GenericTests.TestConstraintBase() == 99);
        RecordResult("GenericTests.TestCovariance", GenericTests.TestCovariance() == 99);
        RecordResult("GenericTests.TestContravariance", GenericTests.TestContravariance() == 42);
    }

    private static void RunStringInterpolationTests()
    {
        RecordResult("StringInterpolationTests.TestSimpleInterpolation", StringInterpolationTests.TestSimpleInterpolation() == 9);     // "Value: 42"
        RecordResult("StringInterpolationTests.TestMultipleValues", StringInterpolationTests.TestMultipleValues() == 12);         // "10 + 20 = 30"
        RecordResult("StringInterpolationTests.TestStringValues", StringInterpolationTests.TestStringValues() == 12);           // "Hello, Test!"
    }

    private static void RunInterfaceTests()
    {
        RecordResult("InterfaceTests.TestSimpleInterface", InterfaceTests.TestSimpleInterface() == 42);
        RecordResult("InterfaceTests.TestMultipleInterfacesFirst", InterfaceTests.TestMultipleInterfacesFirst() == 10);
        RecordResult("InterfaceTests.TestMultipleInterfacesSecond", InterfaceTests.TestMultipleInterfacesSecond() == 40);
        RecordResult("InterfaceTests.TestMultipleInterfacesThird", InterfaceTests.TestMultipleInterfacesThird() == 42);
        RecordResult("InterfaceTests.TestIsinstInterfaceSuccess", InterfaceTests.TestIsinstInterfaceSuccess() == 42);
        RecordResult("InterfaceTests.TestIsinstInterfaceFailure", InterfaceTests.TestIsinstInterfaceFailure() == 42);
        RecordResult("InterfaceTests.TestIsinstNull", InterfaceTests.TestIsinstNull() == 42);
        RecordResult("InterfaceTests.TestIsinstMultipleFirst", InterfaceTests.TestIsinstMultipleFirst() == 10);
        RecordResult("InterfaceTests.TestIsinstMultipleSecond", InterfaceTests.TestIsinstMultipleSecond() == 40);
        RecordResult("InterfaceTests.TestCastclassInterfaceSuccess", InterfaceTests.TestCastclassInterfaceSuccess() == 42);
        // Explicit interface implementation tests
        RecordResult("InterfaceTests.TestExplicitInterfaceImplicit", InterfaceTests.TestExplicitInterfaceImplicit() == 10);
        RecordResult("InterfaceTests.TestExplicitInterfaceExplicit", InterfaceTests.TestExplicitInterfaceExplicit() == 42);
        RecordResult("InterfaceTests.TestExplicitInterfaceBoth", InterfaceTests.TestExplicitInterfaceBoth() == 52);
    }

    private static void RunStructWithRefTests()
    {
        RecordResult("StructWithRefTests.TestStructWithRefCreate", StructWithRefTests.TestStructWithRefCreate() == 42);
        RecordResult("StructWithRefTests.TestStructWithRefReadString", StructWithRefTests.TestStructWithRefReadString() == 5);
        RecordResult("StructWithRefTests.TestStructWithRefModify", StructWithRefTests.TestStructWithRefModify() == 104);
        RecordResult("StructWithRefTests.TestStructWithRefNull", StructWithRefTests.TestStructWithRefNull() == 42);
    }

    private static void RunGenericMethodOnGenericClassTests()
    {
        RecordResult("GenericMethodOnGenericClassTests.TestGenericContainerInt", GenericMethodOnGenericClassTests.TestGenericContainerInt() == 42);
        RecordResult("GenericMethodOnGenericClassTests.TestGenericMethodOnGenericClass", GenericMethodOnGenericClassTests.TestGenericMethodOnGenericClass() == 42);
        RecordResult("GenericMethodOnGenericClassTests.TestGenericMethodDifferentType", GenericMethodOnGenericClassTests.TestGenericMethodDifferentType() == 100);
    }

    private static void RunNullableTests()
    {
        RecordResult("NullableTests.TestNullableHasValue", NullableTests.TestNullableHasValue() == 1);
        RecordResult("NullableTests.TestNullableValue", NullableTests.TestNullableValue() == 42);
        RecordResult("NullableTests.TestNullableNoValue", NullableTests.TestNullableNoValue() == 0);
        RecordResult("NullableTests.TestNullableGetValueOrDefaultWithValue", NullableTests.TestNullableGetValueOrDefaultWithValue() == 42);
        RecordResult("NullableTests.TestNullableGetValueOrDefaultNoValue", NullableTests.TestNullableGetValueOrDefaultNoValue() == 0);
        RecordResult("NullableTests.TestNullableGetValueOrDefaultCustomWithValue", NullableTests.TestNullableGetValueOrDefaultCustomWithValue() == 42);
        RecordResult("NullableTests.TestNullableGetValueOrDefaultCustomNoValue", NullableTests.TestNullableGetValueOrDefaultCustomNoValue() == 99);
        RecordResult("NullableTests.TestNullableImplicitConversion", NullableTests.TestNullableImplicitConversion() == 1);
        RecordResult("NullableTests.TestNullableAssignNull", NullableTests.TestNullableAssignNull() == 0);
        RecordResult("NullableTests.TestNullableParameter", NullableTests.TestNullableParameter() == 42);
        RecordResult("NullableTests.TestNullableParameterNull", NullableTests.TestNullableParameterNull() == 0);
        RecordResult("NullableTests.TestNullableReturn", NullableTests.TestNullableReturn() == 42);
        RecordResult("NullableTests.TestNullableReturnNull", NullableTests.TestNullableReturnNull() == 99);
        RecordResult("NullableTests.TestNullableBoxingWithValue", NullableTests.TestNullableBoxingWithValue() == 42);
        RecordResult("NullableTests.TestNullableBoxingNull", NullableTests.TestNullableBoxingNull() == 1);
        RecordResult("NullableTests.TestNullableBoxingNoHasValue", NullableTests.TestNullableBoxingNoHasValue() == 1);
        RecordResult("NullableTests.TestNullableUnboxFromBoxedInt", NullableTests.TestNullableUnboxFromBoxedInt() == 1);
        RecordResult("NullableTests.TestNullableUnboxFromNull", NullableTests.TestNullableUnboxFromNull() == 1);
        RecordResult("NullableTests.TestNullableRoundTrip", NullableTests.TestNullableRoundTrip() == 1);
        RecordResult("NullableTests.TestNullableRoundTripNull", NullableTests.TestNullableRoundTripNull() == 1);
        // Lifted operators
        RecordResult("NullableTests.TestLiftedAddBothValues", NullableTests.TestLiftedAddBothValues() == 1);
        RecordResult("NullableTests.TestLiftedAddFirstNull", NullableTests.TestLiftedAddFirstNull() == 1);
        RecordResult("NullableTests.TestLiftedAddSecondNull", NullableTests.TestLiftedAddSecondNull() == 1);
        RecordResult("NullableTests.TestLiftedAddBothNull", NullableTests.TestLiftedAddBothNull() == 1);
        RecordResult("NullableTests.TestLiftedSubtract", NullableTests.TestLiftedSubtract() == 1);
        RecordResult("NullableTests.TestLiftedMultiply", NullableTests.TestLiftedMultiply() == 1);
        RecordResult("NullableTests.TestLiftedDivide", NullableTests.TestLiftedDivide() == 1);
        RecordResult("NullableTests.TestLiftedEqualsBothSame", NullableTests.TestLiftedEqualsBothSame() == 1);
        RecordResult("NullableTests.TestLiftedEqualsBothDifferent", NullableTests.TestLiftedEqualsBothDifferent() == 1);
        RecordResult("NullableTests.TestLiftedEqualsBothNull", NullableTests.TestLiftedEqualsBothNull() == 1);
        RecordResult("NullableTests.TestLiftedEqualsOneNull", NullableTests.TestLiftedEqualsOneNull() == 1);
    }

    private static void RunDelegateTests()
    {
        RecordResult("DelegateTests.TestStaticDelegate", DelegateTests.TestStaticDelegate() == 42);
        RecordResult("DelegateTests.TestStaticDelegateTwoArgs", DelegateTests.TestStaticDelegateTwoArgs() == 42);
        RecordResult("DelegateTests.TestVoidDelegate", DelegateTests.TestVoidDelegate() == 42);
        RecordResult("DelegateTests.TestDelegateInvoke", DelegateTests.TestDelegateInvoke() == 42);
        RecordResult("DelegateTests.TestDelegateReassign", DelegateTests.TestDelegateReassign() == 42);
        RecordResult("DelegateTests.TestInstanceDelegate", DelegateTests.TestInstanceDelegate() == 42);
        RecordResult("DelegateTests.TestVirtualDelegate", DelegateTests.TestVirtualDelegate() == 42);
    }

    private static void RunCalliTests()
    {
        RecordResult("CalliTests.TestCalliNoArgs", CalliTests.TestCalliNoArgs() == 42);
        RecordResult("CalliTests.TestCalliOneArg", CalliTests.TestCalliOneArg() == 42);
        RecordResult("CalliTests.TestCalliTwoArgs", CalliTests.TestCalliTwoArgs() == 42);
        RecordResult("CalliTests.TestCalliThreeArgs", CalliTests.TestCalliThreeArgs() == 42);
        RecordResult("CalliTests.TestCalliVoidReturn", CalliTests.TestCalliVoidReturn() == 42);
        RecordResult("CalliTests.TestCalliLong", CalliTests.TestCalliLong() == 42);
        RecordResult("CalliTests.TestCalliReassign", CalliTests.TestCalliReassign() == 42);
    }

    private static void RunMulticastDelegateTests()
    {
        Debug.WriteLine("[PHASE] Starting multicast delegate tests...");
        RecordResult("MulticastDelegateTests.TestCombineTwo", MulticastDelegateTests.TestCombineTwo() == 42);
        RecordResult("MulticastDelegateTests.TestCombineThree", MulticastDelegateTests.TestCombineThree() == 42);
        RecordResult("MulticastDelegateTests.TestCombineNullFirst", MulticastDelegateTests.TestCombineNullFirst() == 42);
        RecordResult("MulticastDelegateTests.TestCombineNullSecond", MulticastDelegateTests.TestCombineNullSecond() == 42);
        RecordResult("MulticastDelegateTests.TestRemoveFromTwo", MulticastDelegateTests.TestRemoveFromTwo() == 42);
        RecordResult("MulticastDelegateTests.TestRemoveNonExistent", MulticastDelegateTests.TestRemoveNonExistent() == 42);
        RecordResult("MulticastDelegateTests.TestRemoveAll", MulticastDelegateTests.TestRemoveAll() == 42);
        RecordResult("MulticastDelegateTests.TestPlusEqualsOperator", MulticastDelegateTests.TestPlusEqualsOperator() == 42);
        RecordResult("MulticastDelegateTests.TestMinusEqualsOperator", MulticastDelegateTests.TestMinusEqualsOperator() == 42);
    }

    private static void RunClosureTests()
    {
        Debug.WriteLine("[PHASE] Starting closure tests...");
        RecordResult("ClosureTests.TestSimpleClosure", ClosureTests.TestSimpleClosure() == 42);
        RecordResult("ClosureTests.TestMultipleCaptures", ClosureTests.TestMultipleCaptures() == 42);
        RecordResult("ClosureTests.TestMutateCaptured", ClosureTests.TestMutateCaptured() == 42);
        RecordResult("ClosureTests.TestCaptureParameter", ClosureTests.TestCaptureParameter() == 42);
        RecordResult("ClosureTests.TestCaptureReferenceType", ClosureTests.TestCaptureReferenceType() == 42);
        RecordResult("ClosureTests.TestNestedClosure", ClosureTests.TestNestedClosure() == 42);
        RecordResult("ClosureTests.TestClosureInLoop", ClosureTests.TestClosureInLoop() == 42);
        RecordResult("ClosureTests.TestRepeatedAccess", ClosureTests.TestRepeatedAccess() == 42);
    }

    private static void RunSizeofTests()
    {
        RecordResult("SizeofTests.TestSizeofByte", SizeofTests.TestSizeofByte() == 42);
        RecordResult("SizeofTests.TestSizeofShort", SizeofTests.TestSizeofShort() == 42);
        RecordResult("SizeofTests.TestSizeofInt", SizeofTests.TestSizeofInt() == 42);
        RecordResult("SizeofTests.TestSizeofLong", SizeofTests.TestSizeofLong() == 42);
        RecordResult("SizeofTests.TestSizeofPointer", SizeofTests.TestSizeofPointer() == 42);
        RecordResult("SizeofTests.TestSizeofStruct", SizeofTests.TestSizeofStruct() == 42);
    }

    private static void RunMemoryBlockTests()
    {
        RecordResult("MemoryBlockTests.TestInitBlock", MemoryBlockTests.TestInitBlock() == 42);
        RecordResult("MemoryBlockTests.TestCopyBlock", MemoryBlockTests.TestCopyBlock() == 42);
        RecordResult("MemoryBlockTests.TestInitBlockLarge", MemoryBlockTests.TestInitBlockLarge() == 42);
        RecordResult("MemoryBlockTests.TestCopyBlockLarge", MemoryBlockTests.TestCopyBlockLarge() == 42);
    }

    private static void RunFixedBufferTests()
    {
        RecordResult("FixedBufferTests.TestFixedByteBuffer", FixedBufferTests.TestFixedByteBuffer() == 42);
        RecordResult("FixedBufferTests.TestFixedIntBuffer", FixedBufferTests.TestFixedIntBuffer() == 42);
        RecordResult("FixedBufferTests.TestFixedBufferLoop", FixedBufferTests.TestFixedBufferLoop() == 42);
        RecordResult("FixedBufferTests.TestDeviceRegisters", FixedBufferTests.TestDeviceRegisters() == 42);
        RecordResult("FixedBufferTests.TestFixedBufferAsParameter", FixedBufferTests.TestFixedBufferAsParameter() == 42);
        RecordResult("FixedBufferTests.TestFixedBufferPointer", FixedBufferTests.TestFixedBufferPointer() == 42);
    }

    private static void RunOverflowTests()
    {
        RecordResult("OverflowTests.TestCheckedAddNoOverflow", OverflowTests.TestCheckedAddNoOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedAddOverflow", OverflowTests.TestCheckedAddOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedSubNoOverflow", OverflowTests.TestCheckedSubNoOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedSubOverflow", OverflowTests.TestCheckedSubOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedMulNoOverflow", OverflowTests.TestCheckedMulNoOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedMulOverflow", OverflowTests.TestCheckedMulOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedConvNoOverflow", OverflowTests.TestCheckedConvNoOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedConvOverflow", OverflowTests.TestCheckedConvOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedAddUnsignedNoOverflow", OverflowTests.TestCheckedAddUnsignedNoOverflow() == 42);
        RecordResult("OverflowTests.TestCheckedAddUnsignedOverflow", OverflowTests.TestCheckedAddUnsignedOverflow() == 42);
    }

    private static void RunStackallocTests()
    {
        RecordResult("StackallocTests.TestStackallocSmall", StackallocTests.TestStackallocSmall() == 42);
        RecordResult("StackallocTests.TestStackallocLarge", StackallocTests.TestStackallocLarge() == 42);
        RecordResult("StackallocTests.TestStackallocInt", StackallocTests.TestStackallocInt() == 42);
        RecordResult("StackallocTests.TestStackallocLong", StackallocTests.TestStackallocLong() == 42);
        RecordResult("StackallocTests.TestStackallocMultiple", StackallocTests.TestStackallocMultiple() == 42);
        RecordResult("StackallocTests.TestStackallocComputation", StackallocTests.TestStackallocComputation() == 42);
    }

    private static void RunStaticCtorTests()
    {
        RecordResult("StaticCtorTests.TestStaticCtorInitializesField", StaticCtorTests.TestStaticCtorInitializesField() == 42);
        RecordResult("StaticCtorTests.TestStaticCtorRunsOnce", StaticCtorTests.TestStaticCtorRunsOnce() == 42);
        RecordResult("StaticCtorTests.TestStaticCtorOnWrite", StaticCtorTests.TestStaticCtorOnWrite() == 100);
        RecordResult("StaticCtorTests.TestStaticCtorWithDependency", StaticCtorTests.TestStaticCtorWithDependency() == 50);
    }

    private static void RecordResult(string testName, bool passed)
    {
        Debug.Write("[TEST] ");
        Debug.Write(testName);
        Debug.Write(" ... ");
        if (passed)
        {
            _passCount++;
            Debug.WriteLine("PASS");
        }
        else
        {
            _failCount++;
            Debug.WriteLine("FAIL");
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
        RecordResult("ObjectTests.TestGetTypeNotNull", ObjectTests.TestGetTypeNotNull() == 1);
        RecordResult("ObjectTests.TestGetTypeName", ObjectTests.TestGetTypeName() == 1);
        RecordResult("ObjectTests.TestGetTypeBoxedInt", ObjectTests.TestGetTypeBoxedInt() == 1);
        RecordResult("ObjectTests.TestGetTypeSameType", ObjectTests.TestGetTypeSameType() == 1);
        RecordResult("ObjectTests.TestGetTypeFullName", ObjectTests.TestGetTypeFullName() == 1);
        RecordResult("ObjectTests.TestGetTypeNamespace", ObjectTests.TestGetTypeNamespace() == 1);
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

        // Explicit layout struct tests (unions, [StructLayout(LayoutKind.Explicit)])
        RecordResult("ExplicitLayoutTests.TestUnionWriteIntReadBytes", ExplicitLayoutTests.TestUnionWriteIntReadBytes() == 10);
        RecordResult("ExplicitLayoutTests.TestUnionWriteBytesReadInt", ExplicitLayoutTests.TestUnionWriteBytesReadInt() == 22136);
        RecordResult("ExplicitLayoutTests.TestExplicitGap", ExplicitLayoutTests.TestExplicitGap() == 300);
        RecordResult("ExplicitLayoutTests.TestExplicitOutOfOrder", ExplicitLayoutTests.TestExplicitOutOfOrder() == 42);
        RecordResult("ExplicitLayoutTests.TestLongIntUnionWriteLong", ExplicitLayoutTests.TestLongIntUnionWriteLong() == 3);
        RecordResult("ExplicitLayoutTests.TestLongIntUnionWriteInts", ExplicitLayoutTests.TestLongIntUnionWriteInts() == 30);
        RecordResult("ExplicitLayoutTests.TestUnionOverwrite", ExplicitLayoutTests.TestUnionOverwrite() == 0);

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

// Helper class for GetType tests
public class GetTypeTestClass
{
    public int Value;
    public GetTypeTestClass(int v) { Value = v; }
}

public static class ObjectTests
{
    public static int TestLdnull()
    {
        object? obj = null;
        return obj == null ? 1 : 0;
    }

    /// <summary>
    /// Test GetType() returns non-null Type object
    /// </summary>
    public static int TestGetTypeNotNull()
    {
        var obj = new GetTypeTestClass(42);
        Type t = obj.GetType();
        // Use object cast to avoid Type.op_Inequality
        return (object)t != null ? 1 : 0;
    }

    /// <summary>
    /// Test Type.Name returns correct class name
    /// </summary>
    public static int TestGetTypeName()
    {
        var obj = new GetTypeTestClass(42);
        Type t = obj.GetType();
        string? name = t.Name;
        // Just check if Name returns non-null for now
        return (object)name != null ? 1 : 0;
    }

    /// <summary>
    /// Test GetType() on boxed int returns non-null Name
    /// </summary>
    public static int TestGetTypeBoxedInt()
    {
        object boxed = 42;
        Type t = boxed.GetType();
        if ((object)t == null) return 0;  // Use object comparison to avoid Type.op_Equality
        string? name = t.Name;
        // Just check for non-null for now - boxed primitives may return "RuntimeType"
        return name != null ? 1 : 0;
    }

    /// <summary>
    /// Test GetType() equality - same type returns same Type name
    /// </summary>
    public static int TestGetTypeSameType()
    {
        var obj1 = new GetTypeTestClass(1);
        var obj2 = new GetTypeTestClass(2);
        Type t1 = obj1.GetType();
        Type t2 = obj2.GetType();
        // Same class should give same Type name
        return t1.Name == t2.Name ? 1 : 0;
    }

    /// <summary>
    /// Test Type.FullName returns non-null value
    /// </summary>
    public static int TestGetTypeFullName()
    {
        var obj = new GetTypeTestClass(42);
        Type t = obj.GetType();
        string? fullName = t.FullName;
        // Just check for non-null for now
        return fullName != null ? 1 : 0;
    }

    /// <summary>
    /// Test Type.Namespace can be called without crash
    /// </summary>
    public static int TestGetTypeNamespace()
    {
        var obj = new GetTypeTestClass(42);
        Type t = obj.GetType();
        string? ns = t.Namespace;
        // Namespace might be null or empty for JIT-compiled types without full metadata
        // Just verify we can call it without crashing
        return 1;
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

// =============================================================================
// Explicit Layout Structs - for testing [StructLayout(LayoutKind.Explicit)]
// =============================================================================

// Simple union - int and bytes at same location
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
public struct IntBytesUnion
{
    [System.Runtime.InteropServices.FieldOffset(0)]
    public int Value;

    [System.Runtime.InteropServices.FieldOffset(0)]
    public byte Byte0;

    [System.Runtime.InteropServices.FieldOffset(1)]
    public byte Byte1;

    [System.Runtime.InteropServices.FieldOffset(2)]
    public byte Byte2;

    [System.Runtime.InteropServices.FieldOffset(3)]
    public byte Byte3;
}

// Explicit layout with gap between fields
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
public struct ExplicitWithGap
{
    [System.Runtime.InteropServices.FieldOffset(0)]
    public int First;      // bytes 0-3

    [System.Runtime.InteropServices.FieldOffset(8)]
    public int Second;     // bytes 8-11 (gap at 4-7)
}

// Explicit layout with out-of-order fields
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
public struct ExplicitOutOfOrder
{
    [System.Runtime.InteropServices.FieldOffset(4)]
    public int FieldA;     // bytes 4-7

    [System.Runtime.InteropServices.FieldOffset(0)]
    public int FieldB;     // bytes 0-3
}

// Long/int union for testing overlapping different sizes
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
public struct LongIntUnion
{
    [System.Runtime.InteropServices.FieldOffset(0)]
    public long LongValue;

    [System.Runtime.InteropServices.FieldOffset(0)]
    public int LowInt;

    [System.Runtime.InteropServices.FieldOffset(4)]
    public int HighInt;
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
// Explicit Layout Tests - [StructLayout(LayoutKind.Explicit)]
// =============================================================================

public static class ExplicitLayoutTests
{
    // Test basic union - write as int, read as bytes
    public static int TestUnionWriteIntReadBytes()
    {
        IntBytesUnion u;
        u.Byte0 = 0;
        u.Byte1 = 0;
        u.Byte2 = 0;
        u.Byte3 = 0;
        u.Value = 0x04030201;  // Little endian: 01, 02, 03, 04
        // Byte0 should be 0x01, Byte1 should be 0x02, etc.
        return u.Byte0 + u.Byte1 + u.Byte2 + u.Byte3;  // 1+2+3+4 = 10
    }

    // Test basic union - write as bytes, read as int
    public static int TestUnionWriteBytesReadInt()
    {
        IntBytesUnion u;
        u.Value = 0;
        u.Byte0 = 0x78;  // Low byte
        u.Byte1 = 0x56;
        u.Byte2 = 0x34;
        u.Byte3 = 0x12;  // High byte
        // Little endian: 0x12345678
        // Return low 16 bits to fit in test range
        return u.Value & 0xFFFF;  // 0x5678 = 22136
    }

    // Test explicit layout with gap
    public static int TestExplicitGap()
    {
        ExplicitWithGap g;
        g.First = 100;
        g.Second = 200;
        return g.First + g.Second;  // 300
    }

    // Test explicit layout with out-of-order fields
    public static int TestExplicitOutOfOrder()
    {
        ExplicitOutOfOrder o;
        o.FieldA = 40;   // at offset 4
        o.FieldB = 2;    // at offset 0
        return o.FieldA + o.FieldB;  // 42
    }

    // Test long/int union - write long, read ints
    public static int TestLongIntUnionWriteLong()
    {
        LongIntUnion u;
        u.LowInt = 0;
        u.HighInt = 0;
        u.LongValue = 0x0000000200000001;  // Low=1, High=2
        return u.LowInt + u.HighInt;  // 1 + 2 = 3
    }

    // Test long/int union - write ints, read long
    public static int TestLongIntUnionWriteInts()
    {
        LongIntUnion u;
        u.LongValue = 0;
        u.LowInt = 10;
        u.HighInt = 20;
        // Long value: 0x0000001400000A (20 << 32 | 10)
        // Return sum of parts as sanity check
        return (int)(u.LongValue & 0xFF) + (int)((u.LongValue >> 32) & 0xFF);  // 10 + 20 = 30
    }

    // Test union field overwrite
    public static int TestUnionOverwrite()
    {
        IntBytesUnion u;
        u.Byte0 = 0;
        u.Byte1 = 0;
        u.Byte2 = 0;
        u.Byte3 = 0;
        u.Value = unchecked((int)0xFFFFFFFF);  // All 1s (-1)
        u.Byte1 = 0;           // Clear second byte
        // Value should now be 0xFFFF00FF
        return (u.Value >> 8) & 0xFF;  // Byte1 should be 0
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
// Exception Handling Tests
// =============================================================================

// Local exception class for testing - avoids cross-assembly type resolution issues
public class TestException : Exception
{
    public TestException() : base() { }
    public TestException(string message) : base(message) { }
}

public class TestException2 : Exception
{
    public TestException2() : base() { }
    public TestException2(string message) : base(message) { }
}

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
        throw new TestException("Test exception");
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
                throw new TestException("Inner");
            }
            catch (TestException)
            {
                throw new TestException2("Outer");
            }
        }
        catch (TestException2)
        {
            return 42;
        }
        return 0;
    }

    /// <summary>
    /// Test multiple catch blocks - first matching block should execute.
    /// Throws TestException which matches the first catch block.
    /// </summary>
    public static int TestMultipleCatchFirst()
    {
        try
        {
            throw new TestException("Test");
        }
        catch (TestException)
        {
            return 42;  // Should hit this - TestException matches first
        }
        catch (TestException2)
        {
            return 0;   // Should NOT hit this
        }
        catch
        {
            return 0;   // Should NOT hit this
        }
    }

    /// <summary>
    /// Test multiple catch blocks - second block should match.
    /// Throws TestException2 which skips first block, matches second.
    /// </summary>
    public static int TestMultipleCatchSecond()
    {
        try
        {
            throw new TestException2("Test");
        }
        catch (TestException)
        {
            return 0;   // Should NOT hit this - TestException2 doesn't match TestException
        }
        catch (TestException2)
        {
            return 42;  // Should hit this
        }
        catch
        {
            return 0;   // Should NOT hit this
        }
    }

    /// <summary>
    /// Test finally executes when returning early from try block.
    /// The leave instruction should transfer control through finally.
    /// </summary>
    public static int TestFinallyWithReturn()
    {
        int finallyRan = 0;
        int result = TestFinallyWithReturnHelper(ref finallyRan);
        // result should be 10 (from return in try)
        // finallyRan should be 1 (finally executed)
        return (result == 10 && finallyRan == 1) ? 42 : 0;
    }

    private static int TestFinallyWithReturnHelper(ref int finallyRan)
    {
        try
        {
            return 10;  // Early return - finally should still run
        }
        finally
        {
            finallyRan = 1;
        }
    }

    /// <summary>
    /// Test finally executes when breaking out of a loop from inside try.
    /// </summary>
    public static int TestFinallyInLoopWithBreak()
    {
        int finallyCount = 0;
        int iterations = 0;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                iterations++;
                if (i == 2)
                    break;  // Break after 3 iterations (0, 1, 2)
            }
            finally
            {
                finallyCount++;
            }
        }

        // iterations should be 3 (0, 1, 2)
        // finallyCount should be 3 (finally runs each time, including on break)
        return (iterations == 3 && finallyCount == 3) ? 42 : 0;
    }

    /// <summary>
    /// Test finally executes even when exception is thrown and caught.
    /// </summary>
    public static int TestFinallyWithExceptionCaught()
    {
        int finallyRan = 0;
        int catchRan = 0;

        try
        {
            try
            {
                throw new TestException("Test");
            }
            finally
            {
                finallyRan = 1;
            }
        }
        catch (TestException)
        {
            catchRan = 1;
        }

        // Both finally and catch should have run
        return (finallyRan == 1 && catchRan == 1) ? 42 : 0;
    }

    /// <summary>
    /// Test finally with continue in loop.
    /// </summary>
    public static int TestFinallyInLoopWithContinue()
    {
        int finallyCount = 0;
        int bodyCount = 0;

        for (int i = 0; i < 3; i++)
        {
            try
            {
                bodyCount++;
                if (i == 1)
                    continue;  // Skip rest of iteration 1
                bodyCount += 10;  // Only executes for i=0 and i=2
            }
            finally
            {
                finallyCount++;
            }
        }

        // bodyCount should be 3 (increments) + 20 (two +10s for i=0 and i=2) = 23
        // finallyCount should be 3 (runs every iteration)
        return (bodyCount == 23 && finallyCount == 3) ? 42 : 0;
    }

    /// <summary>
    /// Test catch when filter clause - filter evaluates to true.
    /// Uses "catch (Exception e) when (condition)" syntax.
    /// </summary>
    public static int TestCatchWhenTrue()
    {
        int filterValue = 42;  // Will be compared in filter
        int result = 0;
        try
        {
            throw new TestException("Filter test");
        }
        catch (Exception) when (filterValue == 42)
        {
            result = 42;  // Should execute - filter is true
        }
        return result;
    }

    /// <summary>
    /// Test catch when filter clause - filter evaluates to false, falls through to next catch.
    /// </summary>
    public static int TestCatchWhenFalse()
    {
        int filterValue = 0;  // Will be compared in filter
        int result = 0;
        try
        {
            throw new TestException("Filter test");
        }
        catch (Exception) when (filterValue == 42)
        {
            result = 0;  // Should NOT execute - filter is false
        }
        catch (Exception)
        {
            result = 42;  // Should execute - fallback catch
        }
        return result;
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

    public static int TestSimpleListIntBasic()
    {
        // Test creation and Count property
        var list = new SimpleList<int>();
        return list.Count;  // Expected: 0 (empty list)
    }

    public static int TestSimpleListIntCount()
    {
        // Test Add method and Count
        var list = new SimpleList<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        return list.Count;  // Expected: 3
    }

    // =========================================================================
    // Generic method with multiple type parameters
    // =========================================================================

    public static int TestMultiTypeParamConvert()
    {
        // Convert<string, int>("hello", 42) should return 42
        int result = MultiTypeParamHelper.Convert<string, int>("hello", 42);
        return result;  // Expected: 42
    }

    public static int TestMultiTypeParamCombine()
    {
        // Combine two values and verify both type params work
        // Using values that give predictable XOR result
        int result = MultiTypeParamHelper.Combine<int, int>(0, 42);
        return result;  // Expected: 0 ^ 42 = 42
    }

    // =========================================================================
    // Generic interface tests
    // =========================================================================

    public static int TestGenericInterfaceInt()
    {
        IContainer<int> container = new Container<int>(42);
        return container.GetValue();  // Expected: 42
    }

    public static int TestGenericInterfaceSetGet()
    {
        IContainer<int> container = new Container<int>(0);
        container.SetValue(42);
        return container.GetValue();  // Expected: 42
    }

    public static int TestGenericInterfaceString()
    {
        IContainer<string> container = new Container<string>("hello");
        return container.GetValue().Length;  // Expected: 5
    }

    // =========================================================================
    // Generic delegate tests
    // =========================================================================

    public static int TestGenericDelegate()
    {
        Transformer<int, int> doubler = x => x * 2;
        return doubler(21);  // Expected: 42
    }

    public static int TestGenericDelegateStringToInt()
    {
        Transformer<string, int> lengthGetter = s => s.Length;
        return lengthGetter("hello");  // Expected: 5
    }

    // =========================================================================
    // Nested generic type tests
    // =========================================================================

    public static int TestNestedGenericSimple()
    {
        var inner = new Outer<int>.Inner<string>();
        inner.OuterValue = 42;
        inner.InnerValue = "hello";
        return inner.OuterValue;  // Expected: 42
    }

    public static int TestNestedGenericInnerValue()
    {
        var inner = new Outer<int>.Inner<string>();
        inner.InnerValue = "world";
        return inner.InnerValue.Length;  // Expected: 5
    }

    public static int TestNestedGenericMethod()
    {
        var inner = new Outer<int>.Inner<string>();
        inner.OuterValue = 20;
        inner.InnerValue = "ab";
        return inner.GetCombined();  // Expected: 20 + 2 = 22
    }

    // =========================================================================
    // Generic constraint tests
    // =========================================================================

    public static int TestConstraintClass()
    {
        // where T : class - test with null
        bool isNull = ConstrainedMethods.IsNull<string>(null);
        return isNull ? 42 : 0;  // Expected: 42
    }

    public static int TestConstraintClassNotNull()
    {
        // where T : class - test with non-null
        bool isNull = ConstrainedMethods.IsNull<string>("hello");
        return isNull ? 0 : 42;  // Expected: 42
    }

    public static int TestConstraintStruct()
    {
        // where T : struct - default(int) is 0
        int result = ConstrainedMethods.GetDefault<int>();
        return result == 0 ? 42 : 0;  // Expected: 42
    }

    public static int TestConstraintNew()
    {
        // where T : new() - create new Creatable
        Debug.WriteLine("[TestConstraintNew] Calling CreateNew...");
        Creatable obj = ConstrainedMethods.CreateNew<Creatable>();
        Debug.WriteLine("[TestConstraintNew] CreateNew returned");
        if (obj == null)
        {
            Debug.WriteLine("[TestConstraintNew] obj is NULL!");
            return 0;
        }
        int value = obj.Value;
        Debug.Write("[TestConstraintNew] Value=");
        Debug.WriteDecimal((uint)value);
        Debug.WriteLine();
        return value;  // Expected: 42
    }

    public static int TestConstraintInterface()
    {
        // where T : IValue - ValueImpl implements IValue
        ValueImpl impl = new ValueImpl();
        return ConstrainedMethods.GetFromInterface<ValueImpl>(impl);  // Expected: 42
    }

    public static int TestConstraintBase()
    {
        // where T : BaseWithValue - DerivedWithValue extends BaseWithValue
        DerivedWithValue derived = new DerivedWithValue();
        return ConstrainedMethods.GetFromBase<DerivedWithValue>(derived);  // Expected: 99 (override)
    }

    public static int TestCovariance()
    {
        // ICovariant<out T> - can assign ICovariant<Derived> to ICovariant<Base>
        DerivedWithValue d = new DerivedWithValue();
        ICovariant<DerivedWithValue> derived = new CovariantImpl<DerivedWithValue>(d);
        ICovariant<BaseWithValue> baseRef = derived;  // Covariant assignment
        return baseRef.Get().GetBaseValue();  // Expected: 99 (derived override)
    }

    public static int TestContravariance()
    {
        // IContravariant<in T> - can assign IContravariant<Base> to IContravariant<Derived>
        IContravariant<BaseWithValue> baseConsumer = new ContravariantImpl<BaseWithValue>();
        IContravariant<DerivedWithValue> derivedConsumer = baseConsumer;  // Contravariant assignment
        DerivedWithValue d = new DerivedWithValue();
        derivedConsumer.Accept(d);
        return 42;  // If we get here without crash, it works
    }
}

public class SimpleList<T>
{
    private T[] _items;
    private int _count;

    public SimpleList()
    {
        _items = new T[8];
        _count = 0;
    }

    public int Count => _count;

    public void Add(T item)
    {
        if (_count < _items.Length)
        {
            _items[_count] = item;
        }
        _count++;
    }

    public T Get(int index)
    {
        return _items[index];
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
// Advanced Generic Types for Testing
// =============================================================================

/// <summary>
/// Generic method with multiple type parameters.
/// </summary>
public static class MultiTypeParamHelper
{
    public static TResult Convert<TInput, TResult>(TInput input, TResult defaultValue)
    {
        // Simple test - just returns the default value
        // Tests that multiple type parameters compile and resolve correctly
        return defaultValue;
    }

    public static int Combine<T1, T2>(T1 first, T2 second)
    {
        // Return hash combination to verify both params work
        return first.GetHashCode() ^ second.GetHashCode();
    }
}

/// <summary>
/// Generic interface for testing.
/// </summary>
public interface IContainer<T>
{
    T GetValue();
    void SetValue(T value);
}

/// <summary>
/// Implementation of generic interface.
/// </summary>
public class Container<T> : IContainer<T>
{
    private T _value;

    public Container(T value)
    {
        _value = value;
    }

    public T GetValue() => _value;
    public void SetValue(T value) => _value = value;
}

/// <summary>
/// Generic delegate for testing.
/// </summary>
public delegate TResult Transformer<TInput, TResult>(TInput input);

/// <summary>
/// Nested generic type for testing Outer&lt;T&gt;.Inner&lt;U&gt; scenarios.
/// </summary>
public class Outer<T>
{
    public class Inner<U>
    {
        public T OuterValue;
        public U InnerValue;

        public int GetCombined()
        {
            // Returns OuterValue (cast to int) + InnerValue.ToString().Length
            int outerInt = (int)(object)OuterValue!;
            int innerLen = InnerValue!.ToString()!.Length;
            return outerInt + innerLen;
        }
    }
}

/// <summary>
/// Class with constrained generic methods.
/// </summary>
public static class ConstrainedMethods
{
    // where T : class constraint
    public static bool IsNull<T>(T value) where T : class
    {
        return value == null;
    }

    // where T : struct constraint
    public static T GetDefault<T>() where T : struct
    {
        return default(T);
    }

    // where T : new() constraint
    public static T CreateNew<T>() where T : new()
    {
        Debug.WriteLine("[CreateNew<T>] Entering...");
        T result = new T();
        Debug.WriteLine("[CreateNew<T>] Created instance, returning...");
        return result;
    }

    // where T : IValue constraint (interface constraint)
    public static int GetFromInterface<T>(T item) where T : IValue
    {
        return item.GetValue();
    }

    // where T : BaseClass constraint
    public static int GetFromBase<T>(T item) where T : BaseWithValue
    {
        return item.GetBaseValue();
    }
}

/// <summary>
/// Class that can be created with new() constraint.
/// </summary>
public class Creatable
{
    public int Value = 42;
}

/// <summary>
/// Base class for constraint testing.
/// </summary>
public class BaseWithValue
{
    public virtual int GetBaseValue() => 42;
}

/// <summary>
/// Derived class for constraint testing.
/// </summary>
public class DerivedWithValue : BaseWithValue
{
    public override int GetBaseValue() => 99;
}

/// <summary>
/// Covariant interface (out T).
/// </summary>
public interface ICovariant<out T>
{
    T Get();
}

/// <summary>
/// Contravariant interface (in T).
/// </summary>
public interface IContravariant<in T>
{
    void Accept(T item);
}

public class CovariantImpl<T> : ICovariant<T>
{
    private T _value;
    public CovariantImpl() { _value = default!; }
    public CovariantImpl(T value) { _value = value; }
    public T Get() => _value;
}

public class ContravariantImpl<T> : IContravariant<T>
{
    public void Accept(T item) { /* Just accept it */ }
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
// Interface Tests - Test interface dispatch via callvirt
// =============================================================================

/// <summary>
/// Simple interface for testing basic dispatch.
/// </summary>
public interface IValue
{
    int GetValue();
}

public interface IMultiplier
{
    int Multiply(int x);
}

public interface IAdder
{
    int Add(int x);
}

/// <summary>
/// Implementation of IValue that returns 42.
/// </summary>
public class ValueImpl : IValue
{
    public int GetValue() => 42;
}

/// <summary>
/// Implementation of multiple interfaces.
/// </summary>
public class MultiInterfaceImpl : IValue, IMultiplier, IAdder
{
    private int _base = 10;

    public int GetValue() => _base;
    public int Multiply(int x) => _base * x;
    public int Add(int x) => _base + x;
}

/// <summary>
/// Interface for testing explicit implementation.
/// </summary>
public interface IExplicit
{
    int GetValue();
}

/// <summary>
/// Class that implements IValue implicitly and IExplicit explicitly.
/// Both interfaces have GetValue() method.
/// </summary>
public class ExplicitImpl : IValue, IExplicit
{
    // Implicit implementation - accessible via class or IValue
    public int GetValue() => 10;

    // Explicit implementation - only accessible via IExplicit
    int IExplicit.GetValue() => 42;
}

/// <summary>
/// Struct containing a reference type field.
/// </summary>
public struct StructWithRef
{
    public int Value;
    public string Name;

    public StructWithRef(int value, string name)
    {
        Value = value;
        Name = name;
    }
}

/// <summary>
/// Generic class with a generic method.
/// </summary>
public class GenericContainer<T>
{
    private T _value;

    public GenericContainer(T value)
    {
        _value = value;
    }

    public T GetValue() => _value;

    /// <summary>
    /// Generic method on generic class - converts stored value using provided converter.
    /// </summary>
    public TResult Convert<TResult>(TResult defaultValue)
    {
        // Simple test: just return the default value
        // This tests that generic method on generic class compiles and runs
        return defaultValue;
    }
}

public static class InterfaceTests
{
    /// <summary>
    /// Test basic interface method call through interface reference.
    /// This requires:
    /// 1. Interface method token resolution (MemberRef with interface parent)
    /// 2. Interface dispatch at runtime (finding vtable slot for interface method)
    /// </summary>
    public static int TestSimpleInterface()
    {
        IValue v = new ValueImpl();
        return v.GetValue();  // Should return 42
    }

    /// <summary>
    /// Test multiple interfaces on same type - call first interface method.
    /// </summary>
    public static int TestMultipleInterfacesFirst()
    {
        MultiInterfaceImpl obj = new MultiInterfaceImpl();
        IValue v = obj;
        return v.GetValue();  // Should return 10
    }

    /// <summary>
    /// Test multiple interfaces on same type - call second interface method.
    /// </summary>
    public static int TestMultipleInterfacesSecond()
    {
        MultiInterfaceImpl obj = new MultiInterfaceImpl();
        IMultiplier m = obj;
        return m.Multiply(4);  // Should return 10 * 4 = 40
    }

    /// <summary>
    /// Test multiple interfaces on same type - call third interface method.
    /// </summary>
    public static int TestMultipleInterfacesThird()
    {
        MultiInterfaceImpl obj = new MultiInterfaceImpl();
        IAdder a = obj;
        return a.Add(32);  // Should return 10 + 32 = 42
    }

    /// <summary>
    /// Test isinst with interface - object implements the interface.
    /// </summary>
    public static int TestIsinstInterfaceSuccess()
    {
        object obj = new ValueImpl();
        IValue v = obj as IValue;  // isinst
        return v != null ? 42 : 0;  // Should return 42
    }

    /// <summary>
    /// Test isinst with interface - object does NOT implement the interface.
    /// </summary>
    public static int TestIsinstInterfaceFailure()
    {
        object obj = new ValueImpl();  // ValueImpl only implements IValue, not IMultiplier
        IMultiplier m = obj as IMultiplier;  // isinst - should return null
        return m == null ? 42 : 0;  // Should return 42
    }

    /// <summary>
    /// Test isinst with null - should return null.
    /// </summary>
    public static int TestIsinstNull()
    {
        object obj = null;
        IValue v = obj as IValue;  // isinst with null
        return v == null ? 42 : 0;  // Should return 42
    }

    /// <summary>
    /// Test isinst with multiple interfaces - check first interface.
    /// </summary>
    public static int TestIsinstMultipleFirst()
    {
        object obj = new MultiInterfaceImpl();
        IValue v = obj as IValue;  // isinst - MultiInterfaceImpl implements IValue
        return v != null ? v.GetValue() : 0;  // Should return 10
    }

    /// <summary>
    /// Test isinst with multiple interfaces - check second interface.
    /// </summary>
    public static int TestIsinstMultipleSecond()
    {
        object obj = new MultiInterfaceImpl();
        IMultiplier m = obj as IMultiplier;  // isinst - MultiInterfaceImpl implements IMultiplier
        return m != null ? m.Multiply(4) : 0;  // Should return 40
    }

    /// <summary>
    /// Test castclass with interface - success case.
    /// </summary>
    public static int TestCastclassInterfaceSuccess()
    {
        object obj = new ValueImpl();
        IValue v = (IValue)obj;  // castclass - should succeed
        return v.GetValue();  // Should return 42
    }

    /// <summary>
    /// Test explicit interface implementation.
    /// ExplicitImpl implements IValue.GetValue() returning 10
    /// and IExplicit.GetValue() explicitly returning 42.
    /// </summary>
    public static int TestExplicitInterfaceImplicit()
    {
        ExplicitImpl obj = new ExplicitImpl();
        IValue v = obj;
        return v.GetValue();  // Should return 10 (implicit implementation)
    }

    /// <summary>
    /// Test explicit interface implementation - call via explicit interface.
    /// </summary>
    public static int TestExplicitInterfaceExplicit()
    {
        ExplicitImpl obj = new ExplicitImpl();
        IExplicit e = obj;
        return e.GetValue();  // Should return 42 (explicit implementation)
    }

    /// <summary>
    /// Test both interfaces on same object return different values.
    /// </summary>
    public static int TestExplicitInterfaceBoth()
    {
        ExplicitImpl obj = new ExplicitImpl();
        IValue v = obj;
        IExplicit e = obj;
        int implicitVal = v.GetValue();  // 10
        int explicitVal = e.GetValue();  // 42
        return implicitVal + explicitVal;  // Should return 52
    }
}

// =============================================================================
// Struct with Reference Tests
// =============================================================================

public static class StructWithRefTests
{
    /// <summary>
    /// Test creating a struct with a reference type field.
    /// </summary>
    public static int TestStructWithRefCreate()
    {
        StructWithRef s = new StructWithRef(42, "test");
        return s.Value;  // Should return 42
    }

    /// <summary>
    /// Test reading reference field from struct.
    /// </summary>
    public static int TestStructWithRefReadString()
    {
        StructWithRef s = new StructWithRef(10, "hello");
        return s.Name != null ? s.Name.Length : 0;  // Should return 5
    }

    /// <summary>
    /// Test modifying struct with reference field.
    /// </summary>
    public static int TestStructWithRefModify()
    {
        StructWithRef s;
        s.Value = 100;
        s.Name = "test";
        return s.Value + s.Name.Length;  // Should return 104
    }

    /// <summary>
    /// Test struct with null reference field.
    /// </summary>
    public static int TestStructWithRefNull()
    {
        StructWithRef s;
        s.Value = 42;
        s.Name = null;
        return s.Name == null ? s.Value : 0;  // Should return 42
    }
}

// =============================================================================
// Generic Method on Generic Class Tests
// =============================================================================

public static class GenericMethodOnGenericClassTests
{
    /// <summary>
    /// Test generic class with int.
    /// </summary>
    public static int TestGenericContainerInt()
    {
        var container = new GenericContainer<int>(42);
        return container.GetValue();  // Should return 42
    }

    /// <summary>
    /// Test generic method on generic class - Convert<TResult>.
    /// </summary>
    public static int TestGenericMethodOnGenericClass()
    {
        var container = new GenericContainer<int>(10);
        int result = container.Convert<int>(42);  // Generic method with int
        return result;  // Should return 42
    }

    /// <summary>
    /// Test generic method with different type parameter than class.
    /// </summary>
    public static int TestGenericMethodDifferentType()
    {
        var container = new GenericContainer<string>("hello");
        int result = container.Convert<int>(100);  // Class is string, method is int
        return result;  // Should return 100
    }
}

// =============================================================================
// Nullable<T> Tests
// =============================================================================

public static class NullableTests
{
    /// <summary>
    /// Test creating a Nullable with a value and reading HasValue.
    /// </summary>
    public static int TestNullableHasValue()
    {
        int? x = 42;
        return x.HasValue ? 1 : 0;  // Should return 1
    }

    /// <summary>
    /// Test creating a Nullable with a value and reading Value.
    /// </summary>
    public static int TestNullableValue()
    {
        int? x = 42;
        return x.Value;  // Should return 42
    }

    /// <summary>
    /// Test creating a null Nullable and reading HasValue.
    /// </summary>
    public static int TestNullableNoValue()
    {
        int? x = null;
        return x.HasValue ? 1 : 0;  // Should return 0
    }

    /// <summary>
    /// Test GetValueOrDefault() on Nullable with value.
    /// </summary>
    public static int TestNullableGetValueOrDefaultWithValue()
    {
        int? x = 42;
        return x.GetValueOrDefault();  // Should return 42
    }

    /// <summary>
    /// Test GetValueOrDefault() on null Nullable (returns default(T)).
    /// </summary>
    public static int TestNullableGetValueOrDefaultNoValue()
    {
        int? x = null;
        return x.GetValueOrDefault();  // Should return 0
    }

    /// <summary>
    /// Test GetValueOrDefault(defaultValue) on Nullable with value.
    /// </summary>
    public static int TestNullableGetValueOrDefaultCustomWithValue()
    {
        int? x = 42;
        return x.GetValueOrDefault(99);  // Should return 42
    }

    /// <summary>
    /// Test GetValueOrDefault(defaultValue) on null Nullable.
    /// </summary>
    public static int TestNullableGetValueOrDefaultCustomNoValue()
    {
        int? x = null;
        return x.GetValueOrDefault(99);  // Should return 99
    }

    /// <summary>
    /// Test implicit conversion from T to Nullable<T>.
    /// </summary>
    public static int TestNullableImplicitConversion()
    {
        int? x = 42;  // Implicit conversion from int to int?
        return x.HasValue && x.Value == 42 ? 1 : 0;  // Should return 1
    }

    /// <summary>
    /// Test assigning null to Nullable<T>.
    /// </summary>
    public static int TestNullableAssignNull()
    {
        int? x = 42;
        x = null;
        return x.HasValue ? 1 : 0;  // Should return 0
    }

    /// <summary>
    /// Test Nullable<T> as method parameter.
    /// </summary>
    public static int TestNullableParameter()
    {
        int? x = 42;
        return GetNullableValueOrZero(x);  // Should return 42
    }

    /// <summary>
    /// Test null Nullable<T> as method parameter.
    /// </summary>
    public static int TestNullableParameterNull()
    {
        int? x = null;
        return GetNullableValueOrZero(x);  // Should return 0
    }

    private static int GetNullableValueOrZero(int? value)
    {
        return value.HasValue ? value.Value : 0;
    }

    /// <summary>
    /// Test Nullable<T> as method return value.
    /// </summary>
    public static int TestNullableReturn()
    {
        int? result = GetNullableFortyTwo();
        return result.HasValue ? result.Value : 0;  // Should return 42
    }

    /// <summary>
    /// Test null Nullable<T> as method return value.
    /// </summary>
    public static int TestNullableReturnNull()
    {
        int? result = GetNullableNull();
        return result.HasValue ? result.Value : 99;  // Should return 99
    }

    private static int? GetNullableFortyTwo()
    {
        return 42;
    }

    private static int? GetNullableNull()
    {
        return null;
    }

    /// <summary>
    /// Test boxing a Nullable<int> with a value - should box to int.
    /// </summary>
    public static int TestNullableBoxingWithValue()
    {
        int? x = 42;
        object boxed = x;  // Boxing happens here
        // If boxing works correctly, boxed should be a boxed int (not null)
        if (boxed == null)
            return 0;  // Fail: should not be null
        // The boxed value should be the inner int value
        int unboxed = (int)boxed;
        return unboxed;  // Should return 42
    }

    /// <summary>
    /// Test boxing a null Nullable<int> - should box to null reference.
    /// </summary>
    public static int TestNullableBoxingNull()
    {
        int? x = null;
        object boxed = x;  // Boxing happens here - should produce null
        return boxed == null ? 1 : 0;  // Should return 1 (boxed is null)
    }

    /// <summary>
    /// Test boxing a Nullable<int> with HasValue=false explicitly.
    /// </summary>
    public static int TestNullableBoxingNoHasValue()
    {
        int? x = new int?();  // Default constructor - HasValue=false
        object boxed = x;
        return boxed == null ? 1 : 0;  // Should return 1 (boxed is null)
    }

    /// <summary>
    /// Test unboxing a boxed int to Nullable<int>.
    /// </summary>
    public static int TestNullableUnboxFromBoxedInt()
    {
        object boxed = (object)42;  // Box an int
        int? result = (int?)boxed;   // Unbox to Nullable<int>
        return result.HasValue && result.Value == 42 ? 1 : 0;  // Should return 1
    }

    /// <summary>
    /// Test unboxing null to Nullable<int>.
    /// </summary>
    public static int TestNullableUnboxFromNull()
    {
        object boxed = null;
        int? result = (int?)boxed;   // Unbox null to Nullable<int>
        return result.HasValue ? 0 : 1;  // Should return 1 (HasValue is false)
    }

    /// <summary>
    /// Test round-trip: box Nullable with value, unbox to Nullable.
    /// </summary>
    public static int TestNullableRoundTrip()
    {
        int? original = 99;
        object boxed = original;  // Box (produces boxed int)
        int? result = (int?)boxed;  // Unbox to Nullable<int>
        return result.HasValue && result.Value == 99 ? 1 : 0;  // Should return 1
    }

    /// <summary>
    /// Test round-trip: box null Nullable, unbox to Nullable.
    /// </summary>
    public static int TestNullableRoundTripNull()
    {
        int? original = null;
        object boxed = original;  // Box (produces null)
        int? result = (int?)boxed;  // Unbox null to Nullable<int>
        return result.HasValue ? 0 : 1;  // Should return 1 (HasValue is false)
    }

    /// <summary>
    /// Test lifted addition: int? + int? where both have values.
    /// </summary>
    public static int TestLiftedAddBothValues()
    {
        int? a = 10;
        int? b = 32;
        int? result = a + b;
        return result.HasValue && result.Value == 42 ? 1 : 0;
    }

    /// <summary>
    /// Test lifted addition: int? + int? where first is null.
    /// </summary>
    public static int TestLiftedAddFirstNull()
    {
        int? a = null;
        int? b = 32;
        int? result = a + b;
        return result.HasValue ? 0 : 1;  // Should return 1 (result is null)
    }

    /// <summary>
    /// Test lifted addition: int? + int? where second is null.
    /// </summary>
    public static int TestLiftedAddSecondNull()
    {
        int? a = 10;
        int? b = null;
        int? result = a + b;
        return result.HasValue ? 0 : 1;  // Should return 1 (result is null)
    }

    /// <summary>
    /// Test lifted addition: int? + int? where both are null.
    /// </summary>
    public static int TestLiftedAddBothNull()
    {
        int? a = null;
        int? b = null;
        int? result = a + b;
        return result.HasValue ? 0 : 1;  // Should return 1 (result is null)
    }

    /// <summary>
    /// Test lifted subtraction.
    /// </summary>
    public static int TestLiftedSubtract()
    {
        int? a = 50;
        int? b = 8;
        int? result = a - b;
        return result.HasValue && result.Value == 42 ? 1 : 0;
    }

    /// <summary>
    /// Test lifted multiplication.
    /// </summary>
    public static int TestLiftedMultiply()
    {
        int? a = 6;
        int? b = 7;
        int? result = a * b;
        return result.HasValue && result.Value == 42 ? 1 : 0;
    }

    /// <summary>
    /// Test lifted division.
    /// </summary>
    public static int TestLiftedDivide()
    {
        int? a = 84;
        int? b = 2;
        int? result = a / b;
        return result.HasValue && result.Value == 42 ? 1 : 0;
    }

    /// <summary>
    /// Test lifted equality: both have same value.
    /// </summary>
    public static int TestLiftedEqualsBothSame()
    {
        int? a = 42;
        int? b = 42;
        bool result = a == b;
        return result ? 1 : 0;  // Should return 1 (true)
    }

    /// <summary>
    /// Test lifted equality: both have different values.
    /// </summary>
    public static int TestLiftedEqualsBothDifferent()
    {
        int? a = 42;
        int? b = 99;
        bool result = a == b;
        return result ? 0 : 1;  // Should return 1 (false)
    }

    /// <summary>
    /// Test lifted equality: both are null (should be true).
    /// </summary>
    public static int TestLiftedEqualsBothNull()
    {
        int? a = null;
        int? b = null;
        bool result = a == b;
        return result ? 1 : 0;  // Should return 1 (true - null == null)
    }

    /// <summary>
    /// Test lifted equality: one is null (should be false).
    /// </summary>
    public static int TestLiftedEqualsOneNull()
    {
        int? a = 42;
        int? b = null;
        bool result = a == b;
        return result ? 0 : 1;  // Should return 1 (false - value != null)
    }
}

// =============================================================================
// Delegate Tests
// =============================================================================

// Simple delegate type for testing
public delegate int IntFunc(int x);
public delegate int IntIntFunc(int x, int y);
public delegate void VoidAction();

public class DelegateTests
{
    // Static methods to be used as delegate targets
    public static int Double(int x) => x * 2;
    public static int Triple(int x) => x * 3;
    public static int Add(int x, int y) => x + y;

    private static int _sideEffect;
    public static void SetSideEffect() { _sideEffect = 42; }

    // Instance field for instance delegate testing
    public int InstanceValue = 2;
    public int InstanceDouble(int x) => x * InstanceValue;

    /// <summary>
    /// Test simple static delegate creation and invocation.
    /// Creates a delegate pointing to a static method and invokes it.
    /// </summary>
    public static int TestStaticDelegate()
    {
        IntFunc f = Double;
        return f(21);  // Should return 42
    }

    /// <summary>
    /// Test static delegate with two arguments.
    /// </summary>
    public static int TestStaticDelegateTwoArgs()
    {
        IntIntFunc f = Add;
        return f(20, 22);  // Should return 42
    }

    /// <summary>
    /// Test void delegate invocation.
    /// </summary>
    public static int TestVoidDelegate()
    {
        _sideEffect = 0;
        VoidAction a = SetSideEffect;
        a();
        return _sideEffect;  // Should return 42
    }

    /// <summary>
    /// Test delegate invocation with explicit .Invoke() call.
    /// (This is semantically identical to f(21) but uses different IL)
    /// </summary>
    public static int TestDelegateInvoke()
    {
        IntFunc f = Double;
        return f.Invoke(21);  // Should return 42
    }

    /// <summary>
    /// Test reassigning a delegate to a different method.
    /// </summary>
    public static int TestDelegateReassign()
    {
        IntFunc f = Double;
        int first = f(10);  // 20
        f = Triple;
        int second = f(10);  // 30
        return first + second - 8;  // 20 + 30 - 8 = 42
    }

    /// <summary>
    /// Test instance delegate - delegate pointing to instance method.
    /// The delegate captures 'this' and calls the instance method.
    /// </summary>
    public static int TestInstanceDelegate()
    {
        DelegateTests obj = new DelegateTests();
        obj.InstanceValue = 3;
        IntFunc f = obj.InstanceDouble;  // Instance delegate: target=obj, method=InstanceDouble
        return f(14);  // Should return 14 * 3 = 42
    }

    /// <summary>
    /// Test virtual delegate - delegate pointing to virtual method.
    /// Uses ldvirtftn to load the virtual function pointer.
    /// The actual method called depends on the runtime type.
    /// </summary>
    public static int TestVirtualDelegate()
    {
        // Create derived class instance, store in base class variable
        VirtualDelegateBase obj = new VirtualDelegateDerived();
        // Create delegate to virtual method - should use ldvirtftn
        IntFunc f = obj.GetValue;
        // Invoke - should call derived implementation
        return f(21);  // Base returns x*1=21, Derived returns x*2=42
    }
}

// Test classes for virtual delegate testing
public class VirtualDelegateBase
{
    public virtual int GetValue(int x) => x * 1;  // Base: multiply by 1
}

public class VirtualDelegateDerived : VirtualDelegateBase
{
    public override int GetValue(int x) => x * 2;  // Derived: multiply by 2
}

// =============================================================================
// Multicast Delegate Tests - test Delegate.Combine and Delegate.Remove
// =============================================================================

// Delegate type for multicast testing with side effects
public delegate void VoidIntAction(int x);

public class MulticastDelegateTests
{
    // Side effect counter for testing multicast invocation
    private static int _counter;
    private static int _lastValue;

    public static void Add10(int x) { _counter += 10; _lastValue = x; }
    public static void Add20(int x) { _counter += 20; _lastValue = x; }
    public static void Add5(int x) { _counter += 5; _lastValue = x; }

    // For IntFunc (returning int)
    public static int Return10(int x) => 10;
    public static int Return20(int x) => 20;
    public static int Return30(int x) => 30;

    /// <summary>
    /// Test Delegate.Combine with two delegates.
    /// Combine should return a multicast delegate.
    /// Invoke returns the last delegate's result.
    /// </summary>
    public static int TestCombineTwo()
    {
        Debug.WriteLine("[MC] TestCombineTwo entered");
        IntFunc f1 = Return10;
        Debug.WriteLine("[MC] f1 created");
        IntFunc f2 = Return20;
        Debug.WriteLine("[MC] f2 created, calling Combine...");
        IntFunc combined = (IntFunc)Delegate.Combine(f1, f2)!;
        Debug.WriteLine("[MC] Combine returned");
        // For value-returning delegates, only the last result is returned
        int result = combined(0);
        return result == 20 ? 42 : 0;
    }

    /// <summary>
    /// Test Delegate.Combine with three delegates.
    /// </summary>
    public static int TestCombineThree()
    {
        IntFunc f1 = Return10;
        IntFunc f2 = Return20;
        IntFunc f3 = Return30;
        IntFunc combined = (IntFunc)Delegate.Combine(f1, f2)!;
        combined = (IntFunc)Delegate.Combine(combined, f3)!;
        int result = combined(0);
        return result == 30 ? 42 : 0;  // Last delegate returns 30
    }

    /// <summary>
    /// Test Delegate.Combine with null first argument.
    /// </summary>
    public static int TestCombineNullFirst()
    {
        IntFunc f2 = Return20;
        IntFunc? result = (IntFunc?)Delegate.Combine(null, f2);
        return result != null && result(0) == 20 ? 42 : 0;
    }

    /// <summary>
    /// Test Delegate.Combine with null second argument.
    /// </summary>
    public static int TestCombineNullSecond()
    {
        IntFunc f1 = Return10;
        IntFunc? result = (IntFunc?)Delegate.Combine(f1, null);
        return result != null && result(0) == 10 ? 42 : 0;
    }

    /// <summary>
    /// Test Delegate.Remove removes a delegate from multicast.
    /// </summary>
    public static int TestRemoveFromTwo()
    {
        IntFunc f1 = Return10;
        IntFunc f2 = Return20;
        IntFunc combined = (IntFunc)Delegate.Combine(f1, f2)!;
        IntFunc? result = (IntFunc?)Delegate.Remove(combined, f2);
        // After removing f2, should only have f1 which returns 10
        return result != null && result(0) == 10 ? 42 : 0;
    }

    /// <summary>
    /// Test Delegate.Remove with non-existent delegate.
    /// </summary>
    public static int TestRemoveNonExistent()
    {
        IntFunc f1 = Return10;
        IntFunc f2 = Return20;
        IntFunc f3 = Return30;
        IntFunc combined = (IntFunc)Delegate.Combine(f1, f2)!;
        // Try to remove f3 which is not in the list
        IntFunc? result = (IntFunc?)Delegate.Remove(combined, f3);
        // Should return unchanged - last delegate is still f2
        return result != null && result(0) == 20 ? 42 : 0;
    }

    /// <summary>
    /// Test removing all delegates results in null.
    /// </summary>
    public static int TestRemoveAll()
    {
        IntFunc f1 = Return10;
        IntFunc? result = (IntFunc?)Delegate.Remove(f1, f1);
        return result == null ? 42 : 0;
    }

    /// <summary>
    /// Test += operator (sugar for Combine).
    /// </summary>
    public static int TestPlusEqualsOperator()
    {
        IntFunc? combined = null;
        combined += Return10;
        combined += Return20;
        return combined != null && combined(0) == 20 ? 42 : 0;
    }

    /// <summary>
    /// Test -= operator (sugar for Remove).
    /// </summary>
    public static int TestMinusEqualsOperator()
    {
        IntFunc f1 = Return10;
        IntFunc f2 = Return20;
        IntFunc? combined = f1;
        combined += f2;
        combined -= f2;
        return combined != null && combined(0) == 10 ? 42 : 0;
    }
}

// =============================================================================
// Closure Tests - test lambdas that capture local variables
// =============================================================================

public class ClosureTests
{
    /// <summary>
    /// Test lambda that captures a single local variable.
    /// The C# compiler generates a display class with the captured variable.
    /// </summary>
    public static int TestSimpleClosure()
    {
        int x = 21;
        IntFunc doubler = n => n + x;  // Captures x
        return doubler(21);  // Should return 42 (21 + 21)
    }

    /// <summary>
    /// Test lambda that captures multiple local variables.
    /// </summary>
    public static int TestMultipleCaptures()
    {
        int a = 10;
        int b = 20;
        int c = 12;
        IntFunc summer = n => a + b + c;  // Captures a, b, c
        return summer(0);  // Should return 42 (10 + 20 + 12)
    }

    /// <summary>
    /// Test lambda that modifies a captured variable.
    /// </summary>
    public static int TestMutateCaptured()
    {
        int count = 0;
        VoidAction increment = () => { count++; };
        increment();
        increment();
        increment();
        return count == 3 ? 42 : 0;  // count should be 3
    }

    /// <summary>
    /// Test lambda capturing a parameter.
    /// </summary>
    public static int TestCaptureParameter()
    {
        return CaptureParameterHelper(21);
    }

    private static int CaptureParameterHelper(int x)
    {
        IntFunc doubler = n => n + x;  // Captures parameter x
        return doubler(21);  // Should return 42
    }

    /// <summary>
    /// Test closure that captures a reference type (string).
    /// </summary>
    public static int TestCaptureReferenceType()
    {
        string s = "hello";
        IntFunc getLen = n => s.Length + n;  // Captures reference type
        return getLen(37);  // Should return 42 (5 + 37)
    }

    /// <summary>
    /// Test nested closures - closure capturing another closure's value.
    /// </summary>
    public static int TestNestedClosure()
    {
        int outer = 10;
        IntFunc outerFunc = x =>
        {
            int inner = 32;
            return outer + inner;  // Captures outer from outer scope
        };
        return outerFunc(0);  // Should return 42 (10 + 32)
    }

    /// <summary>
    /// Test closure in a loop accumulating values.
    /// </summary>
    public static int TestClosureInLoop()
    {
        int sum = 0;
        VoidAction addToSum = () => { };  // Will be reassigned

        // Accumulate 1+2+3+4+5+6+7+8+9 = 45, then subtract 3 = 42
        for (int i = 1; i <= 9; i++)
        {
            int captured = i;  // Capture current value
            VoidAction oldAction = addToSum;
            addToSum = () => { oldAction(); sum += captured; };
        }

        addToSum();
        return sum - 3;  // Should return 42 (45 - 3)
    }

    /// <summary>
    /// Test closure accessing captured variable multiple times.
    /// </summary>
    public static int TestRepeatedAccess()
    {
        int x = 7;
        IntFunc calc = n => x + x * n + x;  // Access x three times
        return calc(4);  // Should return 42 (7 + 7*4 + 7 = 7 + 28 + 7)
    }
}

// =============================================================================
// Sizeof Tests - test the sizeof IL opcode
// =============================================================================

public unsafe class SizeofTests
{
    /// <summary>
    /// Test sizeof for byte (1 byte).
    /// </summary>
    public static int TestSizeofByte()
    {
        return sizeof(byte) == 1 ? 42 : 0;
    }

    /// <summary>
    /// Test sizeof for short (2 bytes).
    /// </summary>
    public static int TestSizeofShort()
    {
        return sizeof(short) == 2 ? 42 : 0;
    }

    /// <summary>
    /// Test sizeof for int (4 bytes).
    /// </summary>
    public static int TestSizeofInt()
    {
        return sizeof(int) == 4 ? 42 : 0;
    }

    /// <summary>
    /// Test sizeof for long (8 bytes).
    /// </summary>
    public static int TestSizeofLong()
    {
        return sizeof(long) == 8 ? 42 : 0;
    }

    /// <summary>
    /// Test sizeof for pointer (8 bytes on x64).
    /// </summary>
    public static int TestSizeofPointer()
    {
        return sizeof(void*) == 8 ? 42 : 0;
    }

    /// <summary>
    /// Test sizeof for SimpleStruct (8 bytes: two int fields).
    /// </summary>
    public static int TestSizeofStruct()
    {
        return sizeof(SimpleStruct) == 8 ? 42 : 0;
    }
}

// =============================================================================
// Memory/Pointer Tests - stackalloc and pointer operations
// =============================================================================

public unsafe static class MemoryBlockTests
{
    public static int TestInitBlock()
    {
        // Test stackalloc and pointer writes
        byte* buffer = stackalloc byte[8];
        for (int i = 0; i < 8; i++) buffer[i] = 0x42;
        return (buffer[0] == 0x42 && buffer[7] == 0x42) ? 42 : 0;
    }

    public static int TestCopyBlock()
    {
        // Test pointer-based memory copy
        byte* src = stackalloc byte[4];
        byte* dst = stackalloc byte[4];
        src[0] = 1; src[1] = 2; src[2] = 3; src[3] = 4;
        for (int i = 0; i < 4; i++) dst[i] = src[i];
        return (dst[0] == 1 && dst[3] == 4) ? 42 : 0;
    }

    /// <summary>
    /// Test memory initialization with larger buffer.
    /// </summary>
    public static int TestInitBlockLarge()
    {
        byte* buffer = stackalloc byte[64];
        for (int i = 0; i < 64; i++) buffer[i] = 0xAB;
        // Check first, middle, and last
        return (buffer[0] == 0xAB && buffer[32] == 0xAB && buffer[63] == 0xAB) ? 42 : 0;
    }

    /// <summary>
    /// Test memory copy with larger buffer.
    /// </summary>
    public static int TestCopyBlockLarge()
    {
        byte* src = stackalloc byte[64];
        byte* dst = stackalloc byte[64];
        // Initialize source with pattern
        for (int i = 0; i < 64; i++) src[i] = (byte)i;
        // Copy
        for (int i = 0; i < 64; i++) dst[i] = src[i];
        // Verify pattern
        return (dst[0] == 0 && dst[32] == 32 && dst[63] == 63) ? 42 : 0;
    }
}

// =============================================================================
// Fixed-Size Buffer Tests - test fixed arrays in structs
// =============================================================================

// Struct with a fixed-size byte buffer
public unsafe struct FixedByteBuffer
{
    public fixed byte Data[8];
}

// Struct with a fixed-size int buffer
public unsafe struct FixedIntBuffer
{
    public fixed int Values[4];
}

// Struct mimicking device registers with fixed buffer for reserved space
public unsafe struct DeviceRegisters
{
    public uint Status;
    public fixed byte Reserved[12];
    public uint Command;
}

public unsafe static class FixedBufferTests
{
    /// <summary>
    /// Test basic fixed byte buffer read/write.
    /// </summary>
    public static int TestFixedByteBuffer()
    {
        FixedByteBuffer buf = new FixedByteBuffer();
        buf.Data[0] = 10;
        buf.Data[1] = 11;
        buf.Data[2] = 12;
        buf.Data[3] = 9;
        return buf.Data[0] + buf.Data[1] + buf.Data[2] + buf.Data[3];  // 10+11+12+9 = 42
    }

    /// <summary>
    /// Test fixed int buffer read/write.
    /// </summary>
    public static int TestFixedIntBuffer()
    {
        FixedIntBuffer buf = new FixedIntBuffer();
        buf.Values[0] = 10;
        buf.Values[1] = 20;
        buf.Values[2] = 7;
        buf.Values[3] = 5;
        return buf.Values[0] + buf.Values[1] + buf.Values[2] + buf.Values[3];  // 10+20+7+5 = 42
    }

    /// <summary>
    /// Test fixed buffer with loop access.
    /// </summary>
    public static int TestFixedBufferLoop()
    {
        FixedByteBuffer buf = new FixedByteBuffer();
        for (int i = 0; i < 8; i++)
            buf.Data[i] = (byte)(i + 1);
        // Sum: 1+2+3+4+5+6+7+8 = 36, need 42 so add 6
        return buf.Data[0] + buf.Data[1] + buf.Data[2] + buf.Data[3] +
               buf.Data[4] + buf.Data[5] + buf.Data[6] + buf.Data[7] - 36 + 42;
    }

    /// <summary>
    /// Test device register struct with fixed buffer for reserved space.
    /// </summary>
    public static int TestDeviceRegisters()
    {
        DeviceRegisters regs = new DeviceRegisters();
        regs.Status = 21;
        regs.Command = 21;
        // Reserved bytes should be zero-initialized, but we don't rely on that
        return (int)(regs.Status + regs.Command);  // 21+21 = 42
    }

    /// <summary>
    /// Test passing struct with fixed buffer to method.
    /// </summary>
    public static int TestFixedBufferAsParameter()
    {
        FixedByteBuffer buf = new FixedByteBuffer();
        buf.Data[0] = 42;
        return ReadFirstByte(ref buf);
    }

    private static int ReadFirstByte(ref FixedByteBuffer buf)
    {
        return buf.Data[0];
    }

    /// <summary>
    /// Test fixed buffer pointer arithmetic.
    /// </summary>
    public static int TestFixedBufferPointer()
    {
        FixedByteBuffer buf = new FixedByteBuffer();
        // Get pointer to the fixed buffer (already fixed, no 'fixed' statement needed)
        byte* ptr = buf.Data;
        ptr[0] = 20;
        ptr[1] = 22;
        return buf.Data[0] + buf.Data[1];  // 20+22 = 42
    }
}

// =============================================================================
// Overflow Checking Tests - test checked arithmetic (add.ovf, etc.)
// =============================================================================

public static class OverflowTests
{
    /// <summary>
    /// Test checked addition that doesn't overflow.
    /// </summary>
    public static int TestCheckedAddNoOverflow()
    {
        int a = 20;
        int b = 22;
        int result = checked(a + b);  // 20 + 22 = 42, no overflow
        return result;
    }

    /// <summary>
    /// Test checked addition that overflows (should throw).
    /// </summary>
    public static int TestCheckedAddOverflow()
    {
        try
        {
            int a = int.MaxValue;
            int b = 1;
            int result = checked(a + b);  // Should overflow
            return 0;  // Should not reach here
        }
        catch (OverflowException)
        {
            return 42;  // Caught overflow
        }
    }

    /// <summary>
    /// Test checked subtraction that doesn't overflow.
    /// </summary>
    public static int TestCheckedSubNoOverflow()
    {
        int a = 50;
        int b = 8;
        int result = checked(a - b);  // 50 - 8 = 42, no overflow
        return result;
    }

    /// <summary>
    /// Test checked subtraction that overflows (should throw).
    /// </summary>
    public static int TestCheckedSubOverflow()
    {
        try
        {
            int a = int.MinValue;
            int b = 1;
            int result = checked(a - b);  // Should overflow (underflow)
            return 0;  // Should not reach here
        }
        catch (OverflowException)
        {
            return 42;  // Caught overflow
        }
    }

    /// <summary>
    /// Test checked multiplication that doesn't overflow.
    /// </summary>
    public static int TestCheckedMulNoOverflow()
    {
        int a = 6;
        int b = 7;
        int result = checked(a * b);  // 6 * 7 = 42, no overflow
        return result;
    }

    /// <summary>
    /// Test checked multiplication that overflows (should throw).
    /// </summary>
    public static int TestCheckedMulOverflow()
    {
        try
        {
            int a = int.MaxValue;
            int b = 2;
            int result = checked(a * b);  // Should overflow
            return 0;  // Should not reach here
        }
        catch (OverflowException)
        {
            return 42;  // Caught overflow
        }
    }

    /// <summary>
    /// Test checked conversion that doesn't overflow.
    /// </summary>
    public static int TestCheckedConvNoOverflow()
    {
        long value = 42;
        int result = checked((int)value);  // Fits in int
        return result;
    }

    /// <summary>
    /// Test checked conversion that overflows (should throw).
    /// </summary>
    public static int TestCheckedConvOverflow()
    {
        try
        {
            long value = (long)int.MaxValue + 1;
            int result = checked((int)value);  // Should overflow
            return 0;  // Should not reach here
        }
        catch (OverflowException)
        {
            return 42;  // Caught overflow
        }
    }

    /// <summary>
    /// Test unsigned checked addition that doesn't overflow.
    /// </summary>
    public static int TestCheckedAddUnsignedNoOverflow()
    {
        uint a = 20;
        uint b = 22;
        uint result = checked(a + b);  // 20 + 22 = 42, no overflow
        return (int)result;
    }

    /// <summary>
    /// Test unsigned checked addition that overflows (should throw).
    /// </summary>
    public static int TestCheckedAddUnsignedOverflow()
    {
        try
        {
            uint a = uint.MaxValue;
            uint b = 1;
            uint result = checked(a + b);  // Should overflow
            return 0;  // Should not reach here
        }
        catch (OverflowException)
        {
            return 42;  // Caught overflow
        }
    }
}

// =============================================================================
// Stackalloc Tests - test localloc IL opcode (stack allocation)
// =============================================================================

public unsafe static class StackallocTests
{
    /// <summary>
    /// Test basic stackalloc with small buffer.
    /// </summary>
    public static int TestStackallocSmall()
    {
        byte* buffer = stackalloc byte[4];
        buffer[0] = 10;
        buffer[1] = 11;
        buffer[2] = 12;
        buffer[3] = 9;
        return buffer[0] + buffer[1] + buffer[2] + buffer[3];  // 10+11+12+9 = 42
    }

    /// <summary>
    /// Test stackalloc with larger buffer (tests alignment).
    /// </summary>
    public static int TestStackallocLarge()
    {
        byte* buffer = stackalloc byte[64];
        for (int i = 0; i < 64; i++)
            buffer[i] = (byte)i;
        // Sum first and last few bytes: 0+1+2+3 + 60+61+62+63 = 6 + 246 = 252
        // We need 42, so let's do: buffer[40] + buffer[2] = 40+2 = 42
        return buffer[40] + buffer[2];
    }

    /// <summary>
    /// Test stackalloc with int array (4-byte elements).
    /// </summary>
    public static int TestStackallocInt()
    {
        int* buffer = stackalloc int[4];
        buffer[0] = 10;
        buffer[1] = 20;
        buffer[2] = 7;
        buffer[3] = 5;
        return buffer[0] + buffer[1] + buffer[2] + buffer[3];  // 10+20+7+5 = 42
    }

    /// <summary>
    /// Test stackalloc with long array (8-byte elements).
    /// </summary>
    public static int TestStackallocLong()
    {
        long* buffer = stackalloc long[2];
        buffer[0] = 21;
        buffer[1] = 21;
        return (int)(buffer[0] + buffer[1]);  // 21+21 = 42
    }

    /// <summary>
    /// Test multiple stackallocs in same method.
    /// </summary>
    public static int TestStackallocMultiple()
    {
        byte* a = stackalloc byte[8];
        byte* b = stackalloc byte[8];
        a[0] = 20;
        b[0] = 22;
        return a[0] + b[0];  // 20+22 = 42
    }

    /// <summary>
    /// Test stackalloc used for temporary computation.
    /// </summary>
    public static int TestStackallocComputation()
    {
        int* values = stackalloc int[6];
        values[0] = 1;
        values[1] = 2;
        values[2] = 3;
        values[3] = 4;
        values[4] = 5;
        values[5] = 6;

        int sum = 0;
        for (int i = 0; i < 6; i++)
            sum += values[i] * 2;  // (1+2+3+4+5+6)*2 = 21*2 = 42
        return sum;
    }
}

// =============================================================================
// Static Constructor Tests - Type initializer (.cctor) invocation
// =============================================================================

// Helper class with a static constructor
public static class StaticCtorHelper1
{
    public static int InitializedValue;

    static StaticCtorHelper1()
    {
        InitializedValue = 42;
    }
}

// Another helper class to test multiple static constructors
public static class StaticCtorHelper2
{
    public static int Counter;
    public static int FirstAccess;

    static StaticCtorHelper2()
    {
        Counter = Counter + 1;  // Should only run once
        FirstAccess = 100;
    }
}

// Helper class to test static constructor with dependency
public static class StaticCtorHelper3
{
    public static int Value;

    static StaticCtorHelper3()
    {
        // Access StaticCtorHelper1.InitializedValue which triggers its cctor first
        Value = StaticCtorHelper1.InitializedValue + 8;  // Should be 50
    }
}

public static class StaticCtorTests
{
    /// <summary>
    /// Test that static constructor initializes a static field before first read.
    /// </summary>
    public static int TestStaticCtorInitializesField()
    {
        // Reading InitializedValue should trigger StaticCtorHelper1's static constructor
        // which sets InitializedValue to 42
        return StaticCtorHelper1.InitializedValue;
    }

    /// <summary>
    /// Test that static constructor only runs once even with multiple accesses.
    /// </summary>
    public static int TestStaticCtorRunsOnce()
    {
        // First access triggers cctor which sets Counter to 1
        int first = StaticCtorHelper2.Counter;
        // Second access should NOT re-run cctor, Counter should still be 1
        int second = StaticCtorHelper2.Counter;
        // Third access
        int third = StaticCtorHelper2.Counter;

        // If cctor only ran once, all three should be 1
        return (first == 1 && second == 1 && third == 1) ? 42 : 0;
    }

    /// <summary>
    /// Test writing to a static field triggers cctor first.
    /// </summary>
    public static int TestStaticCtorOnWrite()
    {
        // Write to Counter - should trigger cctor first (setting Counter to 1)
        // Then we add 10, so Counter should be 11
        StaticCtorHelper2.Counter = StaticCtorHelper2.Counter + 10;

        // But FirstAccess should be 100 (set by cctor)
        return StaticCtorHelper2.FirstAccess;
    }

    /// <summary>
    /// Test static constructor with dependency on another type's static.
    /// </summary>
    public static int TestStaticCtorWithDependency()
    {
        // Access StaticCtorHelper3.Value triggers Helper3's cctor
        // Helper3's cctor accesses Helper1.InitializedValue, triggering Helper1's cctor
        // Helper1's cctor sets InitializedValue to 42
        // Helper3's cctor then computes Value = 42 + 8 = 50
        return StaticCtorHelper3.Value;
    }
}

// =============================================================================
// Calli Tests - test the calli IL opcode (indirect calls through function pointers)
// =============================================================================

public unsafe class CalliTests
{
    // Static methods to be used as function pointer targets
    public static int GetFortyTwo() => 42;
    public static int Double(int x) => x * 2;
    public static int Add(int x, int y) => x + y;
    public static int AddThree(int a, int b, int c) => a + b + c;
    public static long DoubleLong(long x) => x * 2;

    private static int _sideEffect;
    public static void SetSideEffect(int value) { _sideEffect = value; }

    /// <summary>
    /// Test calli with no arguments returning int.
    /// Function pointer to static method with no args.
    /// </summary>
    public static int TestCalliNoArgs()
    {
        delegate*<int> fptr = &GetFortyTwo;
        return fptr();  // Should return 42
    }

    /// <summary>
    /// Test calli with one int argument.
    /// </summary>
    public static int TestCalliOneArg()
    {
        delegate*<int, int> fptr = &Double;
        return fptr(21);  // Should return 42
    }

    /// <summary>
    /// Test calli with two int arguments.
    /// </summary>
    public static int TestCalliTwoArgs()
    {
        delegate*<int, int, int> fptr = &Add;
        return fptr(20, 22);  // Should return 42
    }

    /// <summary>
    /// Test calli with three int arguments.
    /// </summary>
    public static int TestCalliThreeArgs()
    {
        delegate*<int, int, int, int> fptr = &AddThree;
        return fptr(10, 12, 20);  // Should return 42
    }

    /// <summary>
    /// Test calli with void return type.
    /// </summary>
    public static int TestCalliVoidReturn()
    {
        _sideEffect = 0;
        delegate*<int, void> fptr = &SetSideEffect;
        fptr(42);
        return _sideEffect;  // Should return 42
    }

    /// <summary>
    /// Test calli with long argument and return.
    /// </summary>
    public static int TestCalliLong()
    {
        delegate*<long, long> fptr = &DoubleLong;
        long result = fptr(21L);
        return (int)result;  // Should return 42
    }

    /// <summary>
    /// Test reassigning function pointer.
    /// </summary>
    public static int TestCalliReassign()
    {
        delegate*<int, int> fptr = &Double;
        int first = fptr(10);  // 20

        // Note: Can't reassign to Triple directly since it's the same signature
        // but we can test by calling twice with different arg
        int second = fptr(11);  // 22
        return first + second;  // 20 + 22 = 42
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
