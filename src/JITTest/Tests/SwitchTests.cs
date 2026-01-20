// JITTest - Switch Instruction Tests
// Section 7: switch (0x45)

namespace JITTest;

/// <summary>
/// Tests for the switch instruction
/// </summary>
public static class SwitchTests
{
    public static void RunAll()
    {
        TestBasicSwitch();
        TestSwitchBoundary();
        TestSwitchDefault();
        TestSwitchFallthrough();
    }

    private static void TestBasicSwitch()
    {
        TestTracker.Record("switch.Case0", SwitchHelper(0) == 100);
        TestTracker.Record("switch.Case1", SwitchHelper(1) == 101);
        TestTracker.Record("switch.Case2", SwitchHelper(2) == 102);
        TestTracker.Record("switch.Case3", SwitchHelper(3) == 103);
    }

    private static int SwitchHelper(int value)
    {
        switch (value)
        {
            case 0: return 100;
            case 1: return 101;
            case 2: return 102;
            case 3: return 103;
            default: return -1;
        }
    }

    private static void TestSwitchBoundary()
    {
        TestTracker.Record("switch.Negative", SwitchHelper(-1) == -1);
        TestTracker.Record("switch.OutOfRange", SwitchHelper(100) == -1);
    }

    private static void TestSwitchDefault()
    {
        TestTracker.Record("switch.Default", SwitchDefaultHelper(999) == 42);
    }

    private static int SwitchDefaultHelper(int value)
    {
        switch (value)
        {
            case 0: return 0;
            case 1: return 1;
            default: return 42;
        }
    }

    private static void TestSwitchFallthrough()
    {
        // C# doesn't allow fallthrough, but tests the switch instruction behavior
        TestTracker.Record("switch.Sequential", SwitchSequential() == 1);
    }

    private static int SwitchSequential()
    {
        int result = 0;
        for (int i = 0; i < 4; i++)
        {
            switch (i)
            {
                case 0: result += 1; break;
                case 1: result += 10; break;
                case 2: result += 100; break;
                case 3: result += 1000; break;
            }
        }
        return result == 1111 ? 1 : 0;
    }
}
