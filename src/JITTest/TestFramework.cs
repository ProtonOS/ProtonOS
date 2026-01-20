// JITTest - Comprehensive IL Opcode Test Framework for ProtonOS
// Framework infrastructure for ~2,800+ tests covering all 219 IL opcodes

using ProtonOS.DDK.Kernel;

namespace JITTest;

/// <summary>
/// Test result status
/// </summary>
public enum TestStatus
{
    Pass,
    Fail,
    Skip
}

/// <summary>
/// Result of a single test execution
/// </summary>
public readonly struct TestResult
{
    public string Name { get; init; }
    public TestStatus Status { get; init; }
    public string? Message { get; init; }

    public static TestResult Pass(string name) => new() { Name = name, Status = TestStatus.Pass };
    public static TestResult Fail(string name, string? message = null) => new() { Name = name, Status = TestStatus.Fail, Message = message };
    public static TestResult Skip(string name, string? reason = null) => new() { Name = name, Status = TestStatus.Skip, Message = reason };
}

/// <summary>
/// Test category matching JIT_COVERAGE.md sections
/// </summary>
public enum TestCategory
{
    // Section 1-4: Basic operations
    BaseInstructions,       // nop, break, ldarg.0-3, ldloc.0-3, stloc.0-3
    ArgumentLocalShort,     // ldarg.s, ldarga.s, starg.s, ldloc.s, ldloca.s, stloc.s
    ConstantLoading,        // ldnull, ldc.i4.*, ldc.i8, ldc.r4, ldc.r8
    StackManipulation,      // dup, pop

    // Section 5-8: Control flow
    ControlFlowUnconditional, // jmp, call, calli, ret, br.s, br
    ControlFlowConditional,   // brfalse, brtrue, beq, bge, bgt, ble, blt, bne.un, etc.
    SwitchInstruction,        // switch

    // Section 9-10: Indirect memory access
    IndirectLoad,           // ldind.*
    IndirectStore,          // stind.*

    // Section 11-14: Arithmetic and conversion
    Arithmetic,             // add, sub, mul, div, rem, neg, *.ovf
    Bitwise,                // and, or, xor, shl, shr, shr.un, not
    Conversion,             // conv.*

    // Section 15-18: Object model
    MethodCalls,            // call, callvirt, calli
    ObjectOperations,       // newobj, castclass, isinst, box, unbox, throw
    FieldOperations,        // ldfld, stfld, ldsfld, stsfld, ldflda, ldsflda
    StoreObject,            // stobj, ldobj, cpobj

    // Section 19: Arrays
    ArrayOperations,        // newarr, ldlen, ldelem.*, stelem.*, ldelema

    // Section 20-23: Special operations
    TypedReference,         // refanyval, mkrefany, refanytype
    FloatCheck,             // ckfinite
    TokenLoading,           // ldtoken
    ExceptionHandling,      // throw, rethrow, leave, endfinally, endfilter

    // Section 24-25: Two-byte instructions
    Comparison,             // ceq, cgt, clt, *.un
    FunctionPointers,       // ldftn, ldvirtftn

    // Section 26-32: Long form and special
    ArgumentLocalLong,      // ldarg, ldarga, starg, ldloc, ldloca, stloc (long form)
    MemoryAllocation,       // localloc
    PrefixInstructions,     // volatile, unaligned, tail, constrained, readonly
    ObjectInitialization,   // initobj
    BlockOperations,        // cpblk, initblk
    Sizeof,                 // sizeof
    Arglist                 // arglist
}

/// <summary>
/// Tracks test results and provides reporting
/// </summary>
public static class TestTracker
{
    private static int _passCount;
    private static int _failCount;
    private static int _skipCount;
    private static int _totalTests;

    public static int PassCount => _passCount;
    public static int FailCount => _failCount;
    public static int SkipCount => _skipCount;
    public static int TotalTests => _totalTests;

    public static void Reset()
    {
        _passCount = 0;
        _failCount = 0;
        _skipCount = 0;
        _totalTests = 0;
    }

    public static void RecordResult(TestResult result)
    {
        _totalTests++;
        Debug.Write("[TEST] ");
        Debug.Write(result.Name);
        Debug.Write(" ... ");

        switch (result.Status)
        {
            case TestStatus.Pass:
                _passCount++;
                Debug.WriteLine("PASS");
                break;
            case TestStatus.Fail:
                _failCount++;
                Debug.Write("FAIL");
                if (result.Message != null)
                {
                    Debug.Write(" - ");
                    Debug.Write(result.Message);
                }
                Debug.WriteLine("");
                break;
            case TestStatus.Skip:
                _skipCount++;
                Debug.Write("SKIP");
                if (result.Message != null)
                {
                    Debug.Write(" - ");
                    Debug.Write(result.Message);
                }
                Debug.WriteLine("");
                break;
        }
    }

    /// <summary>
    /// Record a simple pass/fail result
    /// </summary>
    public static void Record(string testName, bool passed, string? failMessage = null)
    {
        if (passed)
            RecordResult(TestResult.Pass(testName));
        else
            RecordResult(TestResult.Fail(testName, failMessage));
    }

    /// <summary>
    /// Print summary and return encoded result (pass << 16 | fail)
    /// </summary>
    public static int GetEncodedResult()
    {
        Debug.WriteLine("");
        Debug.Write("[SUMMARY] Total: ");
        Debug.Write(_totalTests.ToString());
        Debug.Write(" | Pass: ");
        Debug.Write(_passCount.ToString());
        Debug.Write(" | Fail: ");
        Debug.Write(_failCount.ToString());
        Debug.Write(" | Skip: ");
        Debug.WriteLine(_skipCount.ToString());

        return (_passCount << 16) | _failCount;
    }
}

/// <summary>
/// Assertion helpers for tests
/// </summary>
public static class Assert
{
    public static bool AreEqual<T>(T expected, T actual) where T : IEquatable<T>
    {
        return expected.Equals(actual);
    }

    public static bool AreEqual(int expected, int actual) => expected == actual;
    public static bool AreEqual(long expected, long actual) => expected == actual;
    public static bool AreEqual(uint expected, uint actual) => expected == actual;
    public static bool AreEqual(ulong expected, ulong actual) => expected == actual;
    public static bool AreEqual(float expected, float actual) => expected == actual;
    public static bool AreEqual(double expected, double actual) => expected == actual;
    public static bool AreEqual(bool expected, bool actual) => expected == actual;

    // Float comparison with tolerance for floating point errors
    public static bool AreApproxEqual(float expected, float actual, float tolerance = 1e-6f)
    {
        if (float.IsNaN(expected) && float.IsNaN(actual)) return true;
        if (float.IsInfinity(expected) && float.IsInfinity(actual))
            return float.IsPositiveInfinity(expected) == float.IsPositiveInfinity(actual);
        return Math.Abs(expected - actual) <= tolerance;
    }

    public static bool AreApproxEqual(double expected, double actual, double tolerance = 1e-10)
    {
        if (double.IsNaN(expected) && double.IsNaN(actual)) return true;
        if (double.IsInfinity(expected) && double.IsInfinity(actual))
            return double.IsPositiveInfinity(expected) == double.IsPositiveInfinity(actual);
        return Math.Abs(expected - actual) <= tolerance;
    }

    public static bool IsTrue(bool condition) => condition;
    public static bool IsFalse(bool condition) => !condition;
    public static bool IsNull(object? obj) => obj is null;
    public static bool IsNotNull(object? obj) => obj is not null;

    // NaN checks
    public static bool IsNaN(float value) => float.IsNaN(value);
    public static bool IsNaN(double value) => double.IsNaN(value);
    public static bool IsPositiveInfinity(float value) => float.IsPositiveInfinity(value);
    public static bool IsPositiveInfinity(double value) => double.IsPositiveInfinity(value);
    public static bool IsNegativeInfinity(float value) => float.IsNegativeInfinity(value);
    public static bool IsNegativeInfinity(double value) => double.IsNegativeInfinity(value);
}
