// JITTest - Multi-Dimensional Array Tests
// Tests 2D and 3D array operations

namespace JITTest;

/// <summary>
/// Multi-dimensional array tests - verifies 2D and 3D array operations.
/// Tests newobj for mdarray, element access via call to Array methods.
/// </summary>
public static class MDArrayTests
{
    public static void RunAll()
    {
        Test2DIntAllocation();
        Test2DIntSetGet();
        Test2DIntZeroed();
        Test2DIntCorners();
        Test2DIntSum();
        Test2DByteSetGet();
        Test2DLongSetGet();
        Test2DDiagonal();
        Test3DIntAllocation();
        Test3DIntSetGet();
        Test3DIntCorners();
        Test3DByteSetGet();
        Test3DIntSum();
        Test2DShortSetGet();
        TestMultiple2DArrays();
    }

    private static void Test2DIntAllocation()
    {
        int[,] arr = new int[3, 4];
        TestTracker.Record("mdarray.2D.Allocation", arr.Length == 12);
    }

    private static void Test2DIntSetGet()
    {
        int[,] arr = new int[3, 4];
        arr[1, 2] = 42;
        TestTracker.Record("mdarray.2D.SetGet", arr[1, 2] == 42);
    }

    private static void Test2DIntZeroed()
    {
        int[,] arr = new int[3, 4];
        bool zeroed = arr[0, 0] == 0 && arr[1, 1] == 0 && arr[2, 3] == 0;
        TestTracker.Record("mdarray.2D.Zeroed", zeroed);
    }

    private static void Test2DIntCorners()
    {
        int[,] arr = new int[3, 4];
        arr[0, 0] = 1;
        arr[0, 3] = 2;
        arr[2, 0] = 3;
        arr[2, 3] = 4;
        int sum = arr[0, 0] + arr[0, 3] + arr[2, 0] + arr[2, 3];
        TestTracker.Record("mdarray.2D.Corners", sum == 10);
    }

    private static void Test2DIntSum()
    {
        int[,] arr = new int[3, 4];
        int val = 1;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                arr[i, j] = val++;
            }
        }

        int sum = 0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                sum += arr[i, j];
            }
        }
        TestTracker.Record("mdarray.2D.Sum", sum == 78);
    }

    private static void Test2DByteSetGet()
    {
        byte[,] arr = new byte[5, 5];
        arr[2, 3] = 200;
        TestTracker.Record("mdarray.2D.Byte", arr[2, 3] == 200);
    }

    private static void Test2DLongSetGet()
    {
        long[,] arr = new long[2, 2];
        arr[1, 1] = 9876543210L;
        TestTracker.Record("mdarray.2D.Long", arr[1, 1] == 9876543210L);
    }

    private static void Test2DDiagonal()
    {
        int[,] arr = new int[4, 4];
        for (int i = 0; i < 4; i++)
        {
            arr[i, i] = (i + 1) * 10;  // 10, 20, 30, 40
        }
        int sum = arr[0, 0] + arr[1, 1] + arr[2, 2] + arr[3, 3];
        TestTracker.Record("mdarray.2D.Diagonal", sum == 100);
    }

    private static void Test3DIntAllocation()
    {
        int[,,] arr = new int[2, 3, 4];
        TestTracker.Record("mdarray.3D.Allocation", arr.Length == 24);
    }

    private static void Test3DIntSetGet()
    {
        int[,,] arr = new int[2, 3, 4];
        arr[1, 2, 3] = 42;
        TestTracker.Record("mdarray.3D.SetGet", arr[1, 2, 3] == 42);
    }

    private static void Test3DIntCorners()
    {
        int[,,] arr = new int[2, 3, 4];
        arr[0, 0, 0] = 1;
        arr[0, 0, 3] = 2;
        arr[0, 2, 0] = 3;
        arr[0, 2, 3] = 4;
        arr[1, 0, 0] = 5;
        arr[1, 0, 3] = 6;
        arr[1, 2, 0] = 7;
        arr[1, 2, 3] = 8;

        int sum = arr[0, 0, 0] + arr[0, 0, 3] + arr[0, 2, 0] + arr[0, 2, 3] +
                  arr[1, 0, 0] + arr[1, 0, 3] + arr[1, 2, 0] + arr[1, 2, 3];
        TestTracker.Record("mdarray.3D.Corners", sum == 36);
    }

    private static void Test3DByteSetGet()
    {
        byte[,,] arr = new byte[3, 3, 3];
        arr[1, 1, 1] = 123;
        TestTracker.Record("mdarray.3D.Byte", arr[1, 1, 1] == 123);
    }

    private static void Test3DIntSum()
    {
        int[,,] arr = new int[2, 2, 2];
        int val = 1;
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    arr[i, j, k] = val++;
                }
            }
        }

        int sum = 0;
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    sum += arr[i, j, k];
                }
            }
        }
        TestTracker.Record("mdarray.3D.Sum", sum == 36);
    }

    private static void Test2DShortSetGet()
    {
        short[,] arr = new short[3, 3];
        arr[1, 1] = 12345;
        TestTracker.Record("mdarray.2D.Short", arr[1, 1] == 12345);
    }

    private static void TestMultiple2DArrays()
    {
        int[,] a = new int[2, 2];
        int[,] b = new int[2, 2];

        a[0, 0] = 10;
        a[1, 1] = 20;
        b[0, 0] = 30;
        b[1, 1] = 40;

        int sum = a[0, 0] + a[1, 1] + b[0, 0] + b[1, 1];
        TestTracker.Record("mdarray.Multiple", sum == 100);
    }
}
