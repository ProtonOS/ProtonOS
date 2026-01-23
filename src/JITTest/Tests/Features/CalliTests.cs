// JITTest - Calli Tests
// Tests the calli IL opcode (indirect calls through function pointers)

namespace JITTest;

/// <summary>
/// Calli tests - verifies function pointer invocation via calli opcode.
/// Tests no args, one arg, two args, three args, void return, long args.
/// </summary>
public unsafe static class CalliTests
{
    private static int _sideEffect;

    public static void RunAll()
    {
        TestCalliNoArgs();
        TestCalliOneArg();
        TestCalliTwoArgs();
        TestCalliThreeArgs();
        TestCalliVoidReturn();
        TestCalliLong();
        TestCalliReassign();
    }

    private static void TestCalliNoArgs()
    {
        delegate*<int> fptr = &GetFortyTwo;
        int result = fptr();
        TestTracker.Record("calli.NoArgs", result == 42);
    }

    private static void TestCalliOneArg()
    {
        delegate*<int, int> fptr = &Double;
        int result = fptr(21);
        TestTracker.Record("calli.OneArg", result == 42);
    }

    private static void TestCalliTwoArgs()
    {
        delegate*<int, int, int> fptr = &Add;
        int result = fptr(20, 22);
        TestTracker.Record("calli.TwoArgs", result == 42);
    }

    private static void TestCalliThreeArgs()
    {
        delegate*<int, int, int, int> fptr = &AddThree;
        int result = fptr(10, 12, 20);
        TestTracker.Record("calli.ThreeArgs", result == 42);
    }

    private static void TestCalliVoidReturn()
    {
        _sideEffect = 0;
        delegate*<int, void> fptr = &SetSideEffect;
        fptr(42);
        TestTracker.Record("calli.VoidReturn", _sideEffect == 42);
    }

    private static void TestCalliLong()
    {
        delegate*<long, long> fptr = &DoubleLong;
        long result = fptr(21L);
        TestTracker.Record("calli.Long", result == 42L);
    }

    private static void TestCalliReassign()
    {
        delegate*<int, int> fptr = &Double;
        int first = fptr(10);  // 20
        int second = fptr(11); // 22
        int sum = first + second;
        TestTracker.Record("calli.Reassign", sum == 42);
    }

    // Helper methods to be used as function pointer targets
    public static int GetFortyTwo() => 42;
    public static int Double(int x) => x * 2;
    public static int Add(int x, int y) => x + y;
    public static int AddThree(int a, int b, int c) => a + b + c;
    public static long DoubleLong(long x) => x * 2;
    public static void SetSideEffect(int value) { _sideEffect = value; }
}
