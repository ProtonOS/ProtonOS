// JITTest - Main Test Runner
// Entry point for comprehensive IL opcode testing

using ProtonOS.DDK.Kernel;

namespace JITTest;

/// <summary>
/// Main test runner - orchestrates all IL opcode tests
/// </summary>
public static class TestRunner
{
    /// <summary>
    /// Main entry point - runs all tests and returns encoded result
    /// Returns: (passCount << 16) | failCount
    /// </summary>
    public static int RunAllTests()
    {
        TestTracker.Reset();

        Debug.WriteLine("===========================================");
        Debug.WriteLine("JITTest - Comprehensive IL Opcode Testing");
        Debug.WriteLine("===========================================");
        Debug.WriteLine("");

        // Section 1: Base Instructions (nop, break, ldarg.0-3, ldloc.0-3, stloc.0-3)
        RunCategory("Base Instructions", BaseInstructionTests.RunAll);

        // Section 2: Argument/Local Short Form
        RunCategory("Argument/Local Short Form", ArgumentLocalShortTests.RunAll);

        // Section 3: Constant Loading
        RunCategory("Constant Loading", ConstantLoadingTests.RunAll);

        // Section 4: Stack Manipulation
        RunCategory("Stack Manipulation", StackManipulationTests.RunAll);

        // Section 5-6: Control Flow
        RunCategory("Control Flow", ControlFlowTests.RunAll);

        // Section 7: Switch
        RunCategory("Switch", SwitchTests.RunAll);

        // Section 9-10: Indirect Load/Store
        RunCategory("Indirect Memory", IndirectMemoryTests.RunAll);

        // Section 11: Arithmetic
        RunCategory("Arithmetic", ArithmeticTests.RunAll);

        // Section 12: Bitwise
        RunCategory("Bitwise", BitwiseTests.RunAll);

        // Section 13: Unary (part of arithmetic)
        // Included in ArithmeticTests

        // Section 14: Conversion
        RunCategory("Conversion", ConversionTests.RunAll);

        // Section 15-16: Object Model
        RunCategory("Object Model", ObjectModelTests.RunAll);

        // Section 17: Field Operations
        RunCategory("Field Operations", FieldOperationTests.RunAll);

        // Section 18: Store Object (ldobj, stobj, cpobj)
        RunCategory("Store Object", StoreObjectTests.RunAll);

        // Section 19: Array Operations
        RunCategory("Array Operations", ArrayOperationTests.RunAll);

        // Section 20: TypedReference
        RunCategory("TypedReference", TypedReferenceTests.RunAll);

        // Section 21: Float Check (ckfinite)
        RunCategory("Float Check", FloatCheckTests.RunAll);

        // Section 22: Token Loading
        RunCategory("Token Loading", TokenLoadingTests.RunAll);

        // Section 23: Exception Handling
        RunCategory("Exception Handling", ExceptionHandlingTests.RunAll);

        // Section 24: Comparison
        RunCategory("Comparison", ComparisonTests.RunAll);

        // Section 25: Function Pointers
        RunCategory("Function Pointers", FunctionPointerTests.RunAll);

        // Section 26: Argument/Local Long Form
        RunCategory("Argument/Local Long Form", ArgumentLocalLongTests.RunAll);

        // Section 27: Memory Allocation (localloc)
        RunCategory("Memory Allocation", MemoryAllocationTests.RunAll);

        // Section 28: Prefix Instructions
        RunCategory("Prefix Instructions", PrefixInstructionTests.RunAll);

        // Section 29: Object Initialization
        RunCategory("Object Initialization", ObjectInitializationTests.RunAll);

        // Section 30: Block Operations
        RunCategory("Block Operations", BlockOperationTests.RunAll);

        // Section 31: Sizeof
        RunCategory("Sizeof", SizeofTests.RunAll);

        // Section 32: Arglist
        RunCategory("Arglist", ArglistTests.RunAll);

        // Section 33: Additional Float Tests
        RunCategory("Additional Float", AdditionalFloatTests.RunAll);

        // Section 34: Additional Struct Tests
        RunCategory("Additional Struct", AdditionalStructTests.RunAll);

        Debug.WriteLine("");
        Debug.WriteLine("===========================================");
        Debug.WriteLine("FEATURE TESTS - High-level JIT Features");
        Debug.WriteLine("===========================================");

        // Feature: Foreach loops on arrays
        RunCategory("Foreach", ForeachTests.RunAll);

        // Feature: Boxing and unboxing
        RunCategory("Boxing", BoxingTests.RunAll);

        // Feature: Generic types and methods
        RunCategory("Generics", GenericTests.RunAll);

        // Feature: Interface dispatch
        RunCategory("Interfaces", InterfaceTests.RunAll);

        // Feature: Nullable<T>
        RunCategory("Nullable", NullableTests.RunAll);

        // Feature: Checked arithmetic (overflow)
        RunCategory("Overflow", OverflowTests.RunAll);

        // Feature: String operations
        RunCategory("Strings", StringTests.RunAll);

        // Feature: Delegates
        RunCategory("Delegates", DelegateTests.RunAll);

        // Feature: Multi-dimensional arrays
        RunCategory("MDArrays", MDArrayTests.RunAll);

        // Feature: Disposable/using pattern
        RunCategory("Disposable", DisposableTests.RunAll);

        // Feature: Static constructors
        RunCategory("StaticCtor", StaticCtorTests.RunAll);

        // Feature: Recursion
        RunCategory("Recursion", RecursionTests.RunAll);

        // Feature: Function pointer calls (calli)
        RunCategory("Calli", CalliTests.RunAll);

        // Feature: Params keyword
        RunCategory("Params", ParamsTests.RunAll);

        // Feature: Custom iterators
        RunCategory("Iterators", IteratorTests.RunAll);

        Debug.WriteLine("");
        Debug.WriteLine("===========================================");
        Debug.WriteLine("KORLIB TESTS - Runtime Library APIs");
        Debug.WriteLine("===========================================");

        // Korlib: List<T>
        RunCategory("List", ListTests.RunAll);

        // Korlib: Dictionary<K,V>
        RunCategory("Dictionary", DictionaryTests.RunAll);

        // Korlib: HashSet<T>
        RunCategory("HashSet", HashSetTests.RunAll);

        // Korlib: StringBuilder
        RunCategory("StringBuilder", StringBuilderTests.RunAll);

        // Korlib: Interlocked
        RunCategory("Interlocked", InterlockedTests.RunAll);

        // Korlib: String formatting
        RunCategory("StringFormat", StringFormatTests.RunAll);

        // Korlib: Utilities (BitConverter, TimeSpan, DateTime, etc.)
        RunCategory("Utilities", UtilityTests.RunAll);

        Debug.WriteLine("");
        Debug.WriteLine("===========================================");
        Debug.WriteLine("REGRESSION TESTS - JIT Bug Fixes");
        Debug.WriteLine("===========================================");

        // Regression: Known JIT issues that were fixed
        RunCategory("JitRegression", JitRegressionTests.RunAll);

        Debug.WriteLine("");
        Debug.WriteLine("===========================================");
        return TestTracker.GetEncodedResult();
    }

    private static void RunCategory(string categoryName, Action testRunner)
    {
        Debug.WriteLine("");
        Debug.Write("[CATEGORY] ");
        Debug.WriteLine(categoryName);
        Debug.WriteLine("-------------------------------------------");

        try
        {
            testRunner();
        }
        catch (Exception ex)
        {
            Debug.Write("[CATEGORY ERROR] ");
            Debug.Write(categoryName);
            Debug.Write(": ");
            Debug.WriteLine(ex.Message);
        }
    }
}
