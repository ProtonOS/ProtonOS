// JITTest - Params Tests
// Tests params keyword (variable argument lists)

namespace JITTest;

/// <summary>
/// Params tests - verifies params keyword functionality.
/// Tests variable argument lists with explicit array construction.
/// </summary>
public static class ParamsTests
{
    public static void RunAll()
    {
        TestParamsExplicitArray();
        TestParamsSingleElement();
        TestParamsEmptyArray();
        TestParamsMixedArgs();
        TestParamsObjectArray();
        TestParamsLength();
    }

    private static void TestParamsExplicitArray()
    {
        int[] arr = new int[5];
        arr[0] = 1; arr[1] = 2; arr[2] = 3; arr[3] = 4; arr[4] = 5;
        int result = SumParams(arr);
        TestTracker.Record("params.ExplicitArray", result == 15);
    }

    private static void TestParamsSingleElement()
    {
        int[] arr = new int[1];
        arr[0] = 42;
        int result = SumParams(arr);
        TestTracker.Record("params.SingleElement", result == 42);
    }

    private static void TestParamsEmptyArray()
    {
        int[] arr = new int[0];
        int result = SumParams(arr);
        TestTracker.Record("params.EmptyArray", result == 0);
    }

    private static void TestParamsMixedArgs()
    {
        int[] arr = new int[4];
        arr[0] = 1; arr[1] = 2; arr[2] = 3; arr[3] = 4;
        int result = SumWithPrefix(100, arr);
        TestTracker.Record("params.MixedArgs", result == 110);
    }

    private static void TestParamsObjectArray()
    {
        object[] arr = new object[3];
        arr[0] = (object)1;
        arr[1] = (object)"hello";
        arr[2] = (object)3;
        int result = CountParams(arr);
        TestTracker.Record("params.ObjectArray", result == 3);
    }

    private static void TestParamsLength()
    {
        int[] arr = new int[7];
        for (int i = 0; i < 7; i++)
            arr[i] = i;
        int result = GetParamsLength(arr);
        TestTracker.Record("params.Length", result == 7);
    }

    // Helper methods
    private static int SumParams(params int[] values)
    {
        int sum = 0;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    private static int SumWithPrefix(int prefix, params int[] values)
    {
        int sum = prefix;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    private static int CountParams(params object[] values)
    {
        return values.Length;
    }

    private static int GetParamsLength(params int[] values)
    {
        return values.Length;
    }
}
