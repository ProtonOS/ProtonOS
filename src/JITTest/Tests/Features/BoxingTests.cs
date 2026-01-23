// JITTest - Boxing Tests
// Tests boxing and unboxing of value types (box, unbox.any IL opcodes)

namespace JITTest;

/// <summary>
/// Boxing tests - verifies box and unbox.any IL opcodes.
/// </summary>
public static class BoxingTests
{
    public static void RunAll()
    {
        TestBoxInt();
        TestBoxStruct();
        TestBoxMediumStruct();
        TestBoxLargeStruct();
    }

    private static void TestBoxInt()
    {
        int x = 42;
        object boxed = x;
        int unboxed = (int)boxed;
        TestTracker.Record("box.Int", unboxed == 42);
    }

    private static void TestBoxStruct()
    {
        BoxTestStruct s;
        s.X = 10;
        s.Y = 20;
        object boxed = s;
        BoxTestStruct unboxed = (BoxTestStruct)boxed;
        TestTracker.Record("box.SmallStruct", unboxed.X + unboxed.Y == 30);
    }

    private static void TestBoxMediumStruct()
    {
        BoxMediumStruct m;
        m.A = 100;
        m.B = 200;
        object boxed = m;
        BoxMediumStruct unboxed = (BoxMediumStruct)boxed;
        TestTracker.Record("box.MediumStruct", unboxed.A + unboxed.B == 300);
    }

    private static void TestBoxLargeStruct()
    {
        BoxLargeStruct l;
        l.A = 10;
        l.B = 20;
        l.C = 30;
        object boxed = l;
        BoxLargeStruct unboxed = (BoxLargeStruct)boxed;
        TestTracker.Record("box.LargeStruct", unboxed.A + unboxed.B + unboxed.C == 60);
    }

    // Supporting types
    private struct BoxTestStruct
    {
        public int X;
        public int Y;
    }

    private struct BoxMediumStruct
    {
        public long A;
        public long B;
    }

    private struct BoxLargeStruct
    {
        public long A;
        public long B;
        public long C;
    }
}
