// JITTest - Foreach Tests
// Tests foreach loops on arrays (generates specific IL patterns)

namespace JITTest;

/// <summary>
/// Foreach tests - verifies foreach loop compilation over arrays.
/// Foreach on arrays compiles to indexed access with bounds checks.
/// </summary>
public static class ForeachTests
{
    public static void RunAll()
    {
        TestForeachIntArray();
        TestForeachByteArray();
        TestForeachEmptyArray();
        TestForeachSingleElement();
        TestForeachLongArray();
        TestForeachObjectArray();
        TestForeachIterationCount();
    }

    private static void TestForeachIntArray()
    {
        int[] arr = new int[5];
        arr[0] = 1; arr[1] = 2; arr[2] = 3; arr[3] = 4; arr[4] = 5;
        int sum = 0;
        foreach (int x in arr)
        {
            sum += x;
        }
        TestTracker.Record("foreach.IntArray", sum == 15);
    }

    private static void TestForeachByteArray()
    {
        byte[] arr = new byte[3];
        arr[0] = 10; arr[1] = 20; arr[2] = 30;
        int sum = 0;
        foreach (byte b in arr)
        {
            sum += b;
        }
        TestTracker.Record("foreach.ByteArray", sum == 60);
    }

    private static void TestForeachEmptyArray()
    {
        int[] arr = new int[0];
        int count = 0;
        foreach (int x in arr)
        {
            count++;
        }
        TestTracker.Record("foreach.EmptyArray", count == 0);
    }

    private static void TestForeachSingleElement()
    {
        int[] arr = new int[1];
        arr[0] = 42;
        int result = 0;
        foreach (int x in arr)
        {
            result = x;
        }
        TestTracker.Record("foreach.SingleElement", result == 42);
    }

    private static void TestForeachLongArray()
    {
        long[] arr = new long[3];
        arr[0] = 10L; arr[1] = 20L; arr[2] = 12L;
        long sum = 0;
        foreach (long l in arr)
        {
            sum += l;
        }
        TestTracker.Record("foreach.LongArray", sum == 42);
    }

    private static void TestForeachObjectArray()
    {
        object[] arr = new object[3];
        arr[0] = "a"; arr[1] = "b"; arr[2] = "c";
        int count = 0;
        foreach (object o in arr)
        {
            if (o != null)
                count++;
        }
        TestTracker.Record("foreach.ObjectArray", count == 3);
    }

    private static void TestForeachIterationCount()
    {
        int[] arr = new int[10];
        arr[0] = 1; arr[1] = 2; arr[2] = 3; arr[3] = 4; arr[4] = 5;
        arr[5] = 6; arr[6] = 7; arr[7] = 8; arr[8] = 9; arr[9] = 10;
        int count = 0;
        foreach (int x in arr)
        {
            count++;
        }
        TestTracker.Record("foreach.IterationCount", count == 10);
    }
}
