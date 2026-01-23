// JITTest - Switch Instruction Tests
// Section 8: switch (0x45)

namespace JITTest;

/// <summary>
/// Tests for the switch instruction (ECMA-335 Section III.3.66)
/// Tests jump table functionality with various case counts and index values
/// </summary>
public static class SwitchTests
{
    public static void RunAll()
    {
        TestBasicSwitch();
        TestSwitchCaseCounts();
        TestSwitchIndexValues();
        TestSwitchFallThrough();
        TestSwitchTargets();
        TestSwitchEdgeCases();
        TestSwitchPatterns();

        // Additional switch tests
        RunAdditionalTests();
    }

    #region Basic Switch Tests

    private static void TestBasicSwitch()
    {
        // Basic functionality
        TestTracker.Record("switch.Case0", SwitchHelper(0) == 100);
        TestTracker.Record("switch.Case1", SwitchHelper(1) == 101);
        TestTracker.Record("switch.Case2", SwitchHelper(2) == 102);
        TestTracker.Record("switch.Case3", SwitchHelper(3) == 103);
        TestTracker.Record("switch.Default", SwitchHelper(999) == -1);
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

    #endregion

    #region Switch Case Counts

    private static void TestSwitchCaseCounts()
    {
        // 2-case switch
        TestTracker.Record("switch.2Cases.0", Switch2Cases(0) == 10);
        TestTracker.Record("switch.2Cases.1", Switch2Cases(1) == 20);
        TestTracker.Record("switch.2Cases.Default", Switch2Cases(2) == -1);

        // 3-case switch
        TestTracker.Record("switch.3Cases.0", Switch3Cases(0) == 100);
        TestTracker.Record("switch.3Cases.1", Switch3Cases(1) == 200);
        TestTracker.Record("switch.3Cases.2", Switch3Cases(2) == 300);
        TestTracker.Record("switch.3Cases.Default", Switch3Cases(3) == -1);

        // 5-case switch
        TestTracker.Record("switch.5Cases.0", Switch5Cases(0) == 0);
        TestTracker.Record("switch.5Cases.2", Switch5Cases(2) == 4);
        TestTracker.Record("switch.5Cases.4", Switch5Cases(4) == 16);
        TestTracker.Record("switch.5Cases.Default", Switch5Cases(5) == -1);

        // 10-case switch
        TestTracker.Record("switch.10Cases.0", Switch10Cases(0) == 0);
        TestTracker.Record("switch.10Cases.5", Switch10Cases(5) == 50);
        TestTracker.Record("switch.10Cases.9", Switch10Cases(9) == 90);
        TestTracker.Record("switch.10Cases.Default", Switch10Cases(10) == -1);
    }

    private static int Switch2Cases(int value)
    {
        switch (value)
        {
            case 0: return 10;
            case 1: return 20;
            default: return -1;
        }
    }

    private static int Switch3Cases(int value)
    {
        switch (value)
        {
            case 0: return 100;
            case 1: return 200;
            case 2: return 300;
            default: return -1;
        }
    }

    private static int Switch5Cases(int value)
    {
        switch (value)
        {
            case 0: return 0;
            case 1: return 1;
            case 2: return 4;
            case 3: return 9;
            case 4: return 16;
            default: return -1;
        }
    }

    private static int Switch10Cases(int value)
    {
        switch (value)
        {
            case 0: return 0;
            case 1: return 10;
            case 2: return 20;
            case 3: return 30;
            case 4: return 40;
            case 5: return 50;
            case 6: return 60;
            case 7: return 70;
            case 8: return 80;
            case 9: return 90;
            default: return -1;
        }
    }

    #endregion

    #region Switch Index Values

    private static void TestSwitchIndexValues()
    {
        // First case (index 0)
        TestTracker.Record("switch.Index0", SwitchHelper(0) == 100);

        // Last valid case
        TestTracker.Record("switch.IndexLast", SwitchHelper(3) == 103);

        // One past last case (fall through)
        TestTracker.Record("switch.IndexPastEnd", SwitchHelper(4) == -1);

        // Way past last case
        TestTracker.Record("switch.IndexFarPast", SwitchHelper(1000) == -1);

        // Negative index (treated as large unsigned)
        TestTracker.Record("switch.IndexNeg1", SwitchHelper(-1) == -1);
        TestTracker.Record("switch.IndexNeg100", SwitchHelper(-100) == -1);

        // Large positive values
        TestTracker.Record("switch.IndexMax", SwitchHelper(int.MaxValue) == -1);
        TestTracker.Record("switch.IndexLarge", SwitchHelper(1000000) == -1);

        // Zero is valid
        TestTracker.Record("switch.IndexZero", SwitchIndexZero(0) == 42);
    }

    private static int SwitchIndexZero(int value)
    {
        switch (value)
        {
            case 0: return 42;
            default: return -1;
        }
    }

    #endregion

    #region Switch Fall-Through

    private static void TestSwitchFallThrough()
    {
        // Fall through to next instruction when out of range
        TestTracker.Record("switch.FallThrough.Positive", TestFallThroughPositive());
        TestTracker.Record("switch.FallThrough.Negative", TestFallThroughNegative());
        TestTracker.Record("switch.FallThrough.WithCode", TestFallThroughWithCode());
    }

    private static bool TestFallThroughPositive()
    {
        int result = SwitchFallThrough(100);
        return result == 999;  // Should hit default
    }

    private static bool TestFallThroughNegative()
    {
        int result = SwitchFallThrough(-5);
        return result == 999;  // Negative treated as out of range, hits default
    }

    private static int SwitchFallThrough(int value)
    {
        switch (value)
        {
            case 0: return 0;
            case 1: return 1;
            case 2: return 2;
            default: return 999;
        }
    }

    private static bool TestFallThroughWithCode()
    {
        int x = 10;
        int result = 0;

        switch (x)
        {
            case 0: result = 100; break;
            case 1: result = 200; break;
            default: result = 300; break;
        }

        // Code after switch should execute
        result += 1;
        return result == 301;
    }

    #endregion

    #region Switch Targets

    private static void TestSwitchTargets()
    {
        // Multiple cases to same target
        TestTracker.Record("switch.SameTarget.0", SwitchSameTarget(0) == 10);
        TestTracker.Record("switch.SameTarget.1", SwitchSameTarget(1) == 10);
        TestTracker.Record("switch.SameTarget.2", SwitchSameTarget(2) == 20);
        TestTracker.Record("switch.SameTarget.3", SwitchSameTarget(3) == 20);

        // Sequential execution (each case independent)
        TestTracker.Record("switch.Sequential", TestSwitchSequential() == 1111);
    }

    private static int SwitchSameTarget(int value)
    {
        switch (value)
        {
            case 0:
            case 1:
                return 10;
            case 2:
            case 3:
                return 20;
            default:
                return -1;
        }
    }

    private static int TestSwitchSequential()
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
        return result;  // 1 + 10 + 100 + 1000 = 1111
    }

    #endregion

    #region Switch Edge Cases

    private static void TestSwitchEdgeCases()
    {
        // Single case switch
        TestTracker.Record("switch.1Case.Match", Switch1Case(0) == 42);
        TestTracker.Record("switch.1Case.NoMatch", Switch1Case(1) == -1);

        // Switch preserves stack below
        TestTracker.Record("switch.PreservesStack", TestSwitchPreservesStack());

        // Nested switches
        TestTracker.Record("switch.Nested", TestNestedSwitch(1, 2) == 12);
        TestTracker.Record("switch.Nested.Other", TestNestedSwitch(0, 1) == 1);

        // Switch in loop
        TestTracker.Record("switch.InLoop", TestSwitchInLoop() == 10);

        // Switch with expression result
        TestTracker.Record("switch.ExprResult", TestSwitchExpression(2) == 20);
    }

    private static int Switch1Case(int value)
    {
        switch (value)
        {
            case 0: return 42;
            default: return -1;
        }
    }

    private static bool TestSwitchPreservesStack()
    {
        int a = 100;
        int b = 200;
        int result = 0;

        switch (1)
        {
            case 0: result = a; break;
            case 1: result = b; break;
        }

        // a and b should still be accessible
        return result == 200 && a == 100 && b == 200;
    }

    private static int TestNestedSwitch(int outer, int inner)
    {
        int result = 0;
        switch (outer)
        {
            case 0:
                switch (inner)
                {
                    case 0: result = 0; break;
                    case 1: result = 1; break;
                    default: result = 2; break;
                }
                break;
            case 1:
                switch (inner)
                {
                    case 0: result = 10; break;
                    case 1: result = 11; break;
                    case 2: result = 12; break;
                    default: result = 13; break;
                }
                break;
            default:
                result = -1;
                break;
        }
        return result;
    }

    private static int TestSwitchInLoop()
    {
        int sum = 0;
        for (int i = 0; i < 5; i++)
        {
            switch (i % 2)
            {
                case 0: sum += 1; break;  // 0, 2, 4 -> 3 times
                case 1: sum += 2; break;  // 1, 3 -> 2 times
            }
        }
        return sum == 7 ? 10 : 0;  // 1+2+1+2+1 = 7
    }

    private static int TestSwitchExpression(int value)
    {
        return value switch
        {
            0 => 0,
            1 => 10,
            2 => 20,
            3 => 30,
            _ => -1
        };
    }

    #endregion

    #region Switch Patterns

    private static void TestSwitchPatterns()
    {
        // Return values from switch
        TestTracker.Record("switch.Return.Case0", SwitchReturn(0) == 'A');
        TestTracker.Record("switch.Return.Case1", SwitchReturn(1) == 'B');
        TestTracker.Record("switch.Return.Default", SwitchReturn(99) == 'X');

        // Switch modifying variable
        TestTracker.Record("switch.Modify", TestSwitchModify() == 150);

        // Switch with complex expressions
        TestTracker.Record("switch.Complex", TestSwitchComplex() == 42);

        // Character switch
        TestTracker.Record("switch.Char.A", SwitchChar('A') == 1);
        TestTracker.Record("switch.Char.B", SwitchChar('B') == 2);
        TestTracker.Record("switch.Char.Default", SwitchChar('Z') == 0);

        // Byte switch
        TestTracker.Record("switch.Byte.0", SwitchByte(0) == 10);
        TestTracker.Record("switch.Byte.255", SwitchByte(255) == 2550);
        TestTracker.Record("switch.Byte.100", SwitchByte(100) == -1);

        // Switch with multiple statements per case
        TestTracker.Record("switch.MultiStmt", TestSwitchMultiStmt(1) == 12);
    }

    private static char SwitchReturn(int value)
    {
        switch (value)
        {
            case 0: return 'A';
            case 1: return 'B';
            case 2: return 'C';
            default: return 'X';
        }
    }

    private static int TestSwitchModify()
    {
        int x = 100;
        switch (1)
        {
            case 0: x += 10; break;
            case 1: x += 50; break;
            case 2: x += 100; break;
        }
        return x;
    }

    private static int TestSwitchComplex()
    {
        int a = 10, b = 20, c = 30;
        int result = 0;

        // Use addition result as switch value
        switch (a + b - c)  // 10 + 20 - 30 = 0
        {
            case 0: result = 42; break;
            case 10: result = 100; break;
            default: result = -1; break;
        }
        return result;
    }

    private static int SwitchChar(char c)
    {
        switch (c)
        {
            case 'A': return 1;
            case 'B': return 2;
            case 'C': return 3;
            default: return 0;
        }
    }

    private static int SwitchByte(byte b)
    {
        switch (b)
        {
            case 0: return 10;
            case 1: return 20;
            case 255: return 2550;
            default: return -1;
        }
    }

    private static int TestSwitchMultiStmt(int value)
    {
        int result = 0;
        switch (value)
        {
            case 0:
                result = 1;
                result *= 2;
                break;
            case 1:
                result = 4;
                result += 5;
                result += 3;
                break;
            default:
                result = -1;
                break;
        }
        return result;  // For case 1: 4 + 5 + 3 = 12
    }

    #endregion

    #region Additional Switch Tests

    public static void RunAdditionalTests()
    {
        TestSwitchLong();
        TestSwitchSByte();
        TestSwitchShort();
        TestSwitchUShort();
        TestSwitchArithmetic();
        TestSwitchWithLocals();
        TestSwitchReturnValue();
    }

    private static void TestSwitchLong()
    {
        // Long switch (may use if-chain instead of jump table)
        TestTracker.Record("switch.long.0", SwitchLong(0L) == 100);
        TestTracker.Record("switch.long.1", SwitchLong(1L) == 101);
        TestTracker.Record("switch.long.2", SwitchLong(2L) == 102);
        TestTracker.Record("switch.long.Default", SwitchLong(999L) == -1);
        TestTracker.Record("switch.long.Negative", SwitchLong(-1L) == -1);
        TestTracker.Record("switch.long.Large", SwitchLong(0x100000000L) == -1);
    }

    private static int SwitchLong(long value)
    {
        switch (value)
        {
            case 0L: return 100;
            case 1L: return 101;
            case 2L: return 102;
            default: return -1;
        }
    }

    private static void TestSwitchSByte()
    {
        // SByte switch
        TestTracker.Record("switch.sbyte.0", SwitchSByte(0) == 10);
        TestTracker.Record("switch.sbyte.1", SwitchSByte(1) == 11);
        TestTracker.Record("switch.sbyte.Neg1", SwitchSByte(-1) == 90);
        TestTracker.Record("switch.sbyte.Max", SwitchSByte(127) == 127);
        TestTracker.Record("switch.sbyte.Min", SwitchSByte(-128) == -128);
        TestTracker.Record("switch.sbyte.Default", SwitchSByte(50) == -1);
    }

    private static int SwitchSByte(sbyte value)
    {
        switch (value)
        {
            case 0: return 10;
            case 1: return 11;
            case -1: return 90;
            case 127: return 127;
            case -128: return -128;
            default: return -1;
        }
    }

    private static void TestSwitchShort()
    {
        // Short switch
        TestTracker.Record("switch.short.0", SwitchShort(0) == 100);
        TestTracker.Record("switch.short.1000", SwitchShort(1000) == 101);
        TestTracker.Record("switch.short.Neg1000", SwitchShort(-1000) == 900);
        TestTracker.Record("switch.short.Max", SwitchShort(32767) == 32767);
        TestTracker.Record("switch.short.Min", SwitchShort(-32768) == -32768);
        TestTracker.Record("switch.short.Default", SwitchShort(500) == -1);
    }

    private static int SwitchShort(short value)
    {
        switch (value)
        {
            case 0: return 100;
            case 1000: return 101;
            case -1000: return 900;
            case 32767: return 32767;
            case -32768: return -32768;
            default: return -1;
        }
    }

    private static void TestSwitchUShort()
    {
        // UShort switch
        TestTracker.Record("switch.ushort.0", SwitchUShort(0) == 100);
        TestTracker.Record("switch.ushort.1000", SwitchUShort(1000) == 101);
        TestTracker.Record("switch.ushort.Max", SwitchUShort(65535) == 65535);
        TestTracker.Record("switch.ushort.32768", SwitchUShort(32768) == 32768);
        TestTracker.Record("switch.ushort.Default", SwitchUShort(500) == -1);
    }

    private static int SwitchUShort(ushort value)
    {
        switch (value)
        {
            case 0: return 100;
            case 1000: return 101;
            case 65535: return 65535;
            case 32768: return 32768;
            default: return -1;
        }
    }

    private static void TestSwitchArithmetic()
    {
        // Switch with arithmetic in cases
        TestTracker.Record("switch.arith.Add", SwitchWithArithmetic(0) == 10);
        TestTracker.Record("switch.arith.Mul", SwitchWithArithmetic(1) == 50);
        TestTracker.Record("switch.arith.Div", SwitchWithArithmetic(2) == 25);
        TestTracker.Record("switch.arith.Mod", SwitchWithArithmetic(3) == 1);
        TestTracker.Record("switch.arith.Default", SwitchWithArithmetic(4) == 99);

        // Switch using computed index
        TestTracker.Record("switch.computed.0", SwitchComputedIndex(10, 10) == 0); // 10-10=0
        TestTracker.Record("switch.computed.1", SwitchComputedIndex(15, 14) == 10); // 15-14=1
        TestTracker.Record("switch.computed.2", SwitchComputedIndex(20, 18) == 20); // 20-18=2
    }

    private static int SwitchWithArithmetic(int value)
    {
        int result = 0;
        switch (value)
        {
            case 0:
                result = 5 + 5;
                break;
            case 1:
                result = 5 * 10;
                break;
            case 2:
                result = 100 / 4;
                break;
            case 3:
                result = 10 % 3;
                break;
            default:
                result = 99;
                break;
        }
        return result;
    }

    private static int SwitchComputedIndex(int a, int b)
    {
        switch (a - b)
        {
            case 0: return 0;
            case 1: return 10;
            case 2: return 20;
            default: return -1;
        }
    }

    private static void TestSwitchWithLocals()
    {
        // Test that locals are preserved across switch
        TestTracker.Record("switch.locals.Before", TestLocalsPreserved(0) == 142);
        TestTracker.Record("switch.locals.After", TestLocalsPreserved(1) == 242);
        TestTracker.Record("switch.locals.Default", TestLocalsPreserved(2) == 42);

        // Test local modification in cases
        TestTracker.Record("switch.localmod.0", TestLocalModification(0) == 110);
        TestTracker.Record("switch.localmod.1", TestLocalModification(1) == 250);
        TestTracker.Record("switch.localmod.2", TestLocalModification(2) == 1000);
    }

    private static int TestLocalsPreserved(int value)
    {
        int a = 42;
        int b = 0;
        switch (value)
        {
            case 0: b = 100; break;
            case 1: b = 200; break;
        }
        return a + b;  // a should still be 42
    }

    private static int TestLocalModification(int value)
    {
        int x = 100;
        switch (value)
        {
            case 0: x += 10; break;
            case 1: x *= 2; x += 50; break;
            case 2: x *= 10; break;
        }
        return x;
    }

    private static void TestSwitchReturnValue()
    {
        // Direct return from switch
        TestTracker.Record("switch.return.0", SwitchDirectReturn(0) == 'A');
        TestTracker.Record("switch.return.1", SwitchDirectReturn(1) == 'B');
        TestTracker.Record("switch.return.Default", SwitchDirectReturn(99) == 'Z');

        // Switch returning computed value
        TestTracker.Record("switch.computed.ret0", SwitchComputedReturn(0) == 1);
        TestTracker.Record("switch.computed.ret1", SwitchComputedReturn(1) == 4);
        TestTracker.Record("switch.computed.ret2", SwitchComputedReturn(2) == 9);
        TestTracker.Record("switch.computed.ret3", SwitchComputedReturn(3) == 16);
    }

    private static char SwitchDirectReturn(int value)
    {
        switch (value)
        {
            case 0: return 'A';
            case 1: return 'B';
            case 2: return 'C';
            case 3: return 'D';
            default: return 'Z';
        }
    }

    private static int SwitchComputedReturn(int value)
    {
        switch (value)
        {
            case 0: return 1 * 1;
            case 1: return 2 * 2;
            case 2: return 3 * 3;
            case 3: return 4 * 4;
            default: return -1;
        }
    }

    #endregion
}
