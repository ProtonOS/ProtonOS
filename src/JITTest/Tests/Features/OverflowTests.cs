// JITTest - Overflow Tests
// Tests checked arithmetic operations (add.ovf, sub.ovf, mul.ovf, conv.ovf IL opcodes)

namespace JITTest;

/// <summary>
/// Overflow tests - verifies checked arithmetic operations.
/// Tests add.ovf, sub.ovf, mul.ovf, conv.ovf IL opcodes.
/// </summary>
public static class OverflowTests
{
    public static void RunAll()
    {
        TestCheckedAddNoOverflow();
        TestCheckedAddOverflow();
        TestCheckedSubNoOverflow();
        TestCheckedSubOverflow();
        TestCheckedMulNoOverflow();
        TestCheckedMulOverflow();
        TestCheckedConvNoOverflow();
        TestCheckedConvOverflow();
        TestCheckedAddUnsignedNoOverflow();
        TestCheckedAddUnsignedOverflow();
    }

    private static void TestCheckedAddNoOverflow()
    {
        int a = 20;
        int b = 22;
        int result = checked(a + b);
        TestTracker.Record("overflow.AddNoOverflow", result == 42);
    }

    private static void TestCheckedAddOverflow()
    {
        bool caught = false;
        try
        {
            int a = int.MaxValue;
            int b = 1;
            int result = checked(a + b);
        }
        catch (OverflowException)
        {
            caught = true;
        }
        TestTracker.Record("overflow.AddOverflow", caught);
    }

    private static void TestCheckedSubNoOverflow()
    {
        int a = 50;
        int b = 8;
        int result = checked(a - b);
        TestTracker.Record("overflow.SubNoOverflow", result == 42);
    }

    private static void TestCheckedSubOverflow()
    {
        bool caught = false;
        try
        {
            int a = int.MinValue;
            int b = 1;
            int result = checked(a - b);
        }
        catch (OverflowException)
        {
            caught = true;
        }
        TestTracker.Record("overflow.SubOverflow", caught);
    }

    private static void TestCheckedMulNoOverflow()
    {
        int a = 6;
        int b = 7;
        int result = checked(a * b);
        TestTracker.Record("overflow.MulNoOverflow", result == 42);
    }

    private static void TestCheckedMulOverflow()
    {
        bool caught = false;
        try
        {
            int a = int.MaxValue;
            int b = 2;
            int result = checked(a * b);
        }
        catch (OverflowException)
        {
            caught = true;
        }
        TestTracker.Record("overflow.MulOverflow", caught);
    }

    private static void TestCheckedConvNoOverflow()
    {
        long value = 42;
        int result = checked((int)value);
        TestTracker.Record("overflow.ConvNoOverflow", result == 42);
    }

    private static void TestCheckedConvOverflow()
    {
        bool caught = false;
        try
        {
            long value = (long)int.MaxValue + 1;
            int result = checked((int)value);
        }
        catch (OverflowException)
        {
            caught = true;
        }
        TestTracker.Record("overflow.ConvOverflow", caught);
    }

    private static void TestCheckedAddUnsignedNoOverflow()
    {
        uint a = 20;
        uint b = 22;
        uint result = checked(a + b);
        TestTracker.Record("overflow.AddUnsignedNoOverflow", result == 42);
    }

    private static void TestCheckedAddUnsignedOverflow()
    {
        bool caught = false;
        try
        {
            uint a = uint.MaxValue;
            uint b = 1;
            uint result = checked(a + b);
        }
        catch (OverflowException)
        {
            caught = true;
        }
        TestTracker.Record("overflow.AddUnsignedOverflow", caught);
    }
}
