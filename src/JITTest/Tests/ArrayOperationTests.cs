// JITTest - Array Operation Tests
// Section 19: newarr, ldlen, ldelem.*, stelem.*, ldelema

namespace JITTest;

/// <summary>
/// Tests for array operation instructions
/// </summary>
public static class ArrayOperationTests
{
    public static void RunAll()
    {
        TestNewarrPrimitives();
        TestNewarrReferences();
        TestNewarrStructs();
        TestNewarrSizes();
        TestLdlen();
        TestLdelemPrimitives();
        TestLdelemReferences();
        TestLdelemBoundary();
        TestStelemPrimitives();
        TestStelemReferences();
        TestStelemOverwrite();
        TestLdelema();
        TestArrayIteration();
        TestAdditionalArrayOps();
    }

    #region Helper Types

    private struct SmallStruct
    {
        public int X;
        public int Y;
    }

    private struct LargeStruct
    {
        public long A;
        public long B;
        public long C;
    }

    #endregion

    #region newarr (0x8D) - Primitives

    private static void TestNewarrPrimitives()
    {
        // Byte/SByte arrays
        var byteArr = new byte[10];
        TestTracker.Record("newarr.Byte", byteArr != null && byteArr.Length == 10);

        var sbyteArr = new sbyte[10];
        TestTracker.Record("newarr.SByte", sbyteArr.Length == 10);

        // Short/UShort arrays
        var shortArr = new short[5];
        TestTracker.Record("newarr.Short", shortArr.Length == 5);

        var ushortArr = new ushort[5];
        TestTracker.Record("newarr.UShort", ushortArr.Length == 5);

        // Int/UInt arrays
        var intArr = new int[10];
        TestTracker.Record("newarr.Int32", intArr.Length == 10);

        var uintArr = new uint[10];
        TestTracker.Record("newarr.UInt32", uintArr.Length == 10);

        // Long/ULong arrays
        var longArr = new long[5];
        TestTracker.Record("newarr.Int64", longArr.Length == 5);

        var ulongArr = new ulong[5];
        TestTracker.Record("newarr.UInt64", ulongArr.Length == 5);

        // Float/Double arrays
        var floatArr = new float[8];
        TestTracker.Record("newarr.Float", floatArr.Length == 8);

        var doubleArr = new double[8];
        TestTracker.Record("newarr.Double", doubleArr.Length == 8);

        // Bool/Char arrays
        var boolArr = new bool[3];
        TestTracker.Record("newarr.Bool", boolArr.Length == 3);

        var charArr = new char[5];
        TestTracker.Record("newarr.Char", charArr.Length == 5);
    }

    #endregion

    #region newarr - References

    private static void TestNewarrReferences()
    {
        // String array
        var strArr = new string[3];
        TestTracker.Record("newarr.String", strArr.Length == 3);

        // Object array
        var objArr = new object[5];
        TestTracker.Record("newarr.Object", objArr.Length == 5);

        // Array of arrays
        var arrArr = new int[3][];
        TestTracker.Record("newarr.ArrayOfArrays", arrArr.Length == 3);

        // All elements initialized to null/default
        TestTracker.Record("newarr.StringDefault", strArr[0] == null);
        TestTracker.Record("newarr.ObjectDefault", objArr[0] == null);
    }

    #endregion

    #region newarr - Structs

    private static void TestNewarrStructs()
    {
        // Small struct array
        var smallArr = new SmallStruct[5];
        TestTracker.Record("newarr.SmallStruct", smallArr.Length == 5);
        TestTracker.Record("newarr.SmallStructDefault", smallArr[0].X == 0 && smallArr[0].Y == 0);

        // Large struct array
        var largeArr = new LargeStruct[3];
        TestTracker.Record("newarr.LargeStruct", largeArr.Length == 3);
        TestTracker.Record("newarr.LargeStructDefault", largeArr[0].A == 0);
    }

    #endregion

    #region newarr - Various Sizes

    private static void TestNewarrSizes()
    {
        // Empty array
        var emptyArr = new int[0];
        TestTracker.Record("newarr.Empty", emptyArr.Length == 0);

        // Single element
        var singleArr = new int[1];
        TestTracker.Record("newarr.Single", singleArr.Length == 1);

        // Small array
        var smallArr = new int[10];
        TestTracker.Record("newarr.Small", smallArr.Length == 10);

        // Medium array
        var mediumArr = new int[100];
        TestTracker.Record("newarr.Medium", mediumArr.Length == 100);

        // Large array
        var largeArr = new int[1000];
        TestTracker.Record("newarr.Large", largeArr.Length == 1000);
    }

    #endregion

    #region ldlen (0x8E)

    private static void TestLdlen()
    {
        // Zero length
        TestTracker.Record("ldlen.Zero", new int[0].Length == 0);

        // Single element
        TestTracker.Record("ldlen.One", new int[1].Length == 1);

        // Various sizes
        TestTracker.Record("ldlen.Ten", new int[10].Length == 10);
        TestTracker.Record("ldlen.Hundred", new int[100].Length == 100);
        TestTracker.Record("ldlen.Thousand", new int[1000].Length == 1000);

        // Different element types
        TestTracker.Record("ldlen.ByteArray", new byte[50].Length == 50);
        TestTracker.Record("ldlen.LongArray", new long[25].Length == 25);
        TestTracker.Record("ldlen.StringArray", new string[15].Length == 15);
        TestTracker.Record("ldlen.StructArray", new SmallStruct[20].Length == 20);
    }

    #endregion

    #region ldelem.* - Primitives

    private static void TestLdelemPrimitives()
    {
        // ldelem.u1 (byte)
        var byteArr = new byte[] { 0, 100, 200, 255 };
        TestTracker.Record("ldelem.u1.First", byteArr[0] == 0);
        TestTracker.Record("ldelem.u1.Middle", byteArr[1] == 100);
        TestTracker.Record("ldelem.u1.Last", byteArr[3] == 255);

        // ldelem.i1 (sbyte)
        var sbyteArr = new sbyte[] { -128, -1, 0, 127 };
        TestTracker.Record("ldelem.i1.Negative", sbyteArr[0] == -128);
        TestTracker.Record("ldelem.i1.Zero", sbyteArr[2] == 0);
        TestTracker.Record("ldelem.i1.Positive", sbyteArr[3] == 127);

        // ldelem.u2 (ushort)
        var ushortArr = new ushort[] { 0, 1000, 50000, 65535 };
        TestTracker.Record("ldelem.u2", ushortArr[2] == 50000);

        // ldelem.i2 (short)
        var shortArr = new short[] { -32768, -1000, 0, 32767 };
        TestTracker.Record("ldelem.i2.Negative", shortArr[0] == -32768);
        TestTracker.Record("ldelem.i2.Positive", shortArr[3] == 32767);

        // ldelem.u4 (uint)
        var uintArr = new uint[] { 0, 42, 3000000000u };
        TestTracker.Record("ldelem.u4", uintArr[1] == 42);

        // ldelem.i4 (int)
        var intArr = new int[] { int.MinValue, -1, 0, 42, int.MaxValue };
        TestTracker.Record("ldelem.i4.Min", intArr[0] == int.MinValue);
        TestTracker.Record("ldelem.i4.Max", intArr[4] == int.MaxValue);
        TestTracker.Record("ldelem.i4.Normal", intArr[3] == 42);

        // ldelem.i8 (long)
        var longArr = new long[] { long.MinValue, 0, 0x123456789ABCDEF0L, long.MaxValue };
        TestTracker.Record("ldelem.i8.Min", longArr[0] == long.MinValue);
        TestTracker.Record("ldelem.i8.Pattern", longArr[2] == 0x123456789ABCDEF0L);

        // ldelem.r4 (float)
        var floatArr = new float[] { 0.0f, 1.0f, 3.14f, -1.5f };
        TestTracker.Record("ldelem.r4.Zero", floatArr[0] == 0.0f);
        TestTracker.Record("ldelem.r4.One", floatArr[1] == 1.0f);
        TestTracker.Record("ldelem.r4.Pi", floatArr[2] == 3.14f);

        // ldelem.r8 (double)
        var doubleArr = new double[] { 0.0, 1.0, 3.14159265358979, -2.71828 };
        TestTracker.Record("ldelem.r8.Zero", doubleArr[0] == 0.0);
        TestTracker.Record("ldelem.r8.Pi", doubleArr[2] == 3.14159265358979);

        // Bool
        var boolArr = new bool[] { true, false, true };
        TestTracker.Record("ldelem.Bool.True", boolArr[0] == true);
        TestTracker.Record("ldelem.Bool.False", boolArr[1] == false);

        // Char
        var charArr = new char[] { 'A', 'B', 'Z', '\0' };
        TestTracker.Record("ldelem.Char", charArr[0] == 'A');
        TestTracker.Record("ldelem.CharNull", charArr[3] == '\0');
    }

    #endregion

    #region ldelem - References

    private static void TestLdelemReferences()
    {
        // String array
        var strArr = new string[] { "first", "second", "third" };
        TestTracker.Record("ldelem.ref.String", strArr[0] == "first");
        TestTracker.Record("ldelem.ref.StringMiddle", strArr[1] == "second");

        // Object array
        var objArr = new object[] { "string", 42, null };
        TestTracker.Record("ldelem.ref.Object", (string?)objArr[0] == "string");
        TestTracker.Record("ldelem.ref.BoxedInt", (int)objArr[1] == 42);
        TestTracker.Record("ldelem.ref.Null", objArr[2] == null);

        // Struct array
        var structArr = new SmallStruct[] {
            new SmallStruct { X = 1, Y = 2 },
            new SmallStruct { X = 10, Y = 20 }
        };
        TestTracker.Record("ldelem.Struct", structArr[0].X == 1 && structArr[0].Y == 2);
        TestTracker.Record("ldelem.StructSecond", structArr[1].X == 10);
    }

    #endregion

    #region ldelem - Boundary

    private static void TestLdelemBoundary()
    {
        var arr = new int[] { 100, 200, 300, 400, 500 };

        // First element
        TestTracker.Record("ldelem.First", arr[0] == 100);

        // Last element
        TestTracker.Record("ldelem.Last", arr[4] == 500);

        // Computed index
        int idx = 2;
        TestTracker.Record("ldelem.ComputedIndex", arr[idx] == 300);

        // Index from expression
        TestTracker.Record("ldelem.ExpressionIndex", arr[1 + 1] == 300);
    }

    #endregion

    #region stelem.* - Primitives

    private static void TestStelemPrimitives()
    {
        // stelem for byte
        var byteArr = new byte[3];
        byteArr[0] = 0;
        byteArr[1] = 128;
        byteArr[2] = 255;
        TestTracker.Record("stelem.u1", byteArr[0] == 0 && byteArr[1] == 128 && byteArr[2] == 255);

        // stelem for sbyte
        var sbyteArr = new sbyte[3];
        sbyteArr[0] = -128;
        sbyteArr[1] = 0;
        sbyteArr[2] = 127;
        TestTracker.Record("stelem.i1", sbyteArr[0] == -128 && sbyteArr[2] == 127);

        // stelem for short
        var shortArr = new short[3];
        shortArr[0] = -1000;
        shortArr[1] = 0;
        shortArr[2] = 1000;
        TestTracker.Record("stelem.i2", shortArr[0] == -1000 && shortArr[2] == 1000);

        // stelem for int
        var intArr = new int[3];
        intArr[0] = int.MinValue;
        intArr[1] = 42;
        intArr[2] = int.MaxValue;
        TestTracker.Record("stelem.i4.Min", intArr[0] == int.MinValue);
        TestTracker.Record("stelem.i4.Normal", intArr[1] == 42);
        TestTracker.Record("stelem.i4.Max", intArr[2] == int.MaxValue);

        // stelem for long
        var longArr = new long[2];
        longArr[0] = 0x123456789ABCDEF0L;
        longArr[1] = long.MaxValue;
        TestTracker.Record("stelem.i8", longArr[0] == 0x123456789ABCDEF0L);

        // stelem for float
        var floatArr = new float[2];
        floatArr[0] = 3.14f;
        floatArr[1] = -1.5f;
        TestTracker.Record("stelem.r4", floatArr[0] == 3.14f && floatArr[1] == -1.5f);

        // stelem for double
        var doubleArr = new double[2];
        doubleArr[0] = 3.14159265358979;
        doubleArr[1] = -2.71828;
        TestTracker.Record("stelem.r8", doubleArr[0] == 3.14159265358979);

        // stelem for bool
        var boolArr = new bool[2];
        boolArr[0] = true;
        boolArr[1] = false;
        TestTracker.Record("stelem.Bool", boolArr[0] == true && boolArr[1] == false);

        // stelem for char
        var charArr = new char[2];
        charArr[0] = 'X';
        charArr[1] = 'Y';
        TestTracker.Record("stelem.Char", charArr[0] == 'X' && charArr[1] == 'Y');
    }

    #endregion

    #region stelem - References

    private static void TestStelemReferences()
    {
        // Store strings
        var strArr = new string?[3];
        strArr[0] = "first";
        strArr[1] = "second";
        strArr[2] = null;
        TestTracker.Record("stelem.ref.String", strArr[0] == "first" && strArr[1] == "second");
        TestTracker.Record("stelem.ref.Null", strArr[2] == null);

        // Store objects
        var objArr = new object?[3];
        objArr[0] = "string";
        objArr[1] = 42;
        objArr[2] = new int[] { 1, 2, 3 };
        TestTracker.Record("stelem.ref.MixedTypes", (string?)objArr[0] == "string" && (int)objArr[1]! == 42);

        // Store structs
        var structArr = new SmallStruct[2];
        structArr[0] = new SmallStruct { X = 10, Y = 20 };
        structArr[1] = new SmallStruct { X = 30, Y = 40 };
        TestTracker.Record("stelem.Struct", structArr[0].X == 10 && structArr[1].Y == 40);
    }

    #endregion

    #region stelem - Overwrite

    private static void TestStelemOverwrite()
    {
        var arr = new int[3];

        // Initial store
        arr[0] = 100;
        TestTracker.Record("stelem.Initial", arr[0] == 100);

        // Overwrite
        arr[0] = 200;
        TestTracker.Record("stelem.Overwrite", arr[0] == 200);

        // Multiple overwrites
        arr[0] = 1;
        arr[0] = 2;
        arr[0] = 3;
        TestTracker.Record("stelem.MultiOverwrite", arr[0] == 3);

        // Overwrite all elements
        var arr2 = new int[] { 1, 2, 3 };
        arr2[0] = 10;
        arr2[1] = 20;
        arr2[2] = 30;
        TestTracker.Record("stelem.OverwriteAll", arr2[0] == 10 && arr2[1] == 20 && arr2[2] == 30);
    }

    #endregion

    #region ldelema (0x8F)

    private static unsafe void TestLdelema()
    {
        // Basic read via address
        var intArr = new int[] { 1, 2, 42, 4 };
        fixed (int* ptr = &intArr[2])
        {
            TestTracker.Record("ldelema.Read", *ptr == 42);
        }

        // Write via address
        fixed (int* ptr = &intArr[2])
        {
            *ptr = 100;
        }
        TestTracker.Record("ldelema.Write", intArr[2] == 100);

        // Address of first element
        fixed (int* ptr = &intArr[0])
        {
            TestTracker.Record("ldelema.First", *ptr == 1);
        }

        // Address of last element
        fixed (int* ptr = &intArr[3])
        {
            TestTracker.Record("ldelema.Last", *ptr == 4);
        }

        // Long array
        var longArr = new long[] { 0x123456789ABCDEF0L, 0 };
        fixed (long* ptr = &longArr[0])
        {
            TestTracker.Record("ldelema.Long", *ptr == 0x123456789ABCDEF0L);
        }

        // Struct array
        var structArr = new SmallStruct[] { new SmallStruct { X = 10, Y = 20 } };
        fixed (SmallStruct* ptr = &structArr[0])
        {
            TestTracker.Record("ldelema.StructRead", ptr->X == 10 && ptr->Y == 20);
            ptr->X = 100;
        }
        TestTracker.Record("ldelema.StructWrite", structArr[0].X == 100);

        // Ref parameter pattern
        var refArr = new int[] { 42 };
        IncrementArrayElement(ref refArr[0]);
        TestTracker.Record("ldelema.RefParam", refArr[0] == 43);
    }

    private static void IncrementArrayElement(ref int value) => value++;

    #endregion

    #region Array Iteration

    private static void TestArrayIteration()
    {
        // Sum elements with for loop
        var arr = new int[] { 1, 2, 3, 4, 5 };
        int sum = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            sum += arr[i];
        }
        TestTracker.Record("array.ForLoopSum", sum == 15);

        // Copy elements
        var src = new int[] { 10, 20, 30 };
        var dst = new int[3];
        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = src[i];
        }
        TestTracker.Record("array.Copy", dst[0] == 10 && dst[1] == 20 && dst[2] == 30);

        // Find element
        var searchArr = new int[] { 5, 10, 15, 20, 25 };
        int foundIdx = -1;
        for (int i = 0; i < searchArr.Length; i++)
        {
            if (searchArr[i] == 15)
            {
                foundIdx = i;
                break;
            }
        }
        TestTracker.Record("array.Find", foundIdx == 2);

        // Count elements matching condition
        var countArr = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        int count = 0;
        for (int i = 0; i < countArr.Length; i++)
        {
            if (countArr[i] % 2 == 0)
                count++;
        }
        TestTracker.Record("array.CountEven", count == 5);

        // Additional iteration tests
        // Reverse copy
        var revSrc = new int[] { 1, 2, 3, 4, 5 };
        var revDst = new int[5];
        for (int i = 0; i < revSrc.Length; i++)
        {
            revDst[revSrc.Length - 1 - i] = revSrc[i];
        }
        TestTracker.Record("array.ReverseCopy", revDst[0] == 5 && revDst[4] == 1);

        // Max element
        var maxArr = new int[] { 5, 9, 3, 7, 2 };
        int max = maxArr[0];
        for (int i = 1; i < maxArr.Length; i++)
        {
            if (maxArr[i] > max)
                max = maxArr[i];
        }
        TestTracker.Record("array.FindMax", max == 9);

        // Min element
        var minArr = new int[] { 5, 9, 3, 7, 2 };
        int min = minArr[0];
        for (int i = 1; i < minArr.Length; i++)
        {
            if (minArr[i] < min)
                min = minArr[i];
        }
        TestTracker.Record("array.FindMin", min == 2);

        // Product
        var prodArr = new int[] { 2, 3, 4 };
        int product = 1;
        for (int i = 0; i < prodArr.Length; i++)
        {
            product *= prodArr[i];
        }
        TestTracker.Record("array.Product", product == 24);

        // Count zeros
        var zeroArr = new int[] { 0, 1, 0, 2, 0, 3 };
        int zeros = 0;
        for (int i = 0; i < zeroArr.Length; i++)
        {
            if (zeroArr[i] == 0)
                zeros++;
        }
        TestTracker.Record("array.CountZeros", zeros == 3);

        // All positive check
        var posArr = new int[] { 1, 2, 3, 4 };
        bool allPositive = true;
        for (int i = 0; i < posArr.Length; i++)
        {
            if (posArr[i] <= 0)
            {
                allPositive = false;
                break;
            }
        }
        TestTracker.Record("array.AllPositive", allPositive);

        // Contains negative check
        var negArr = new int[] { 1, -2, 3 };
        bool hasNegative = false;
        for (int i = 0; i < negArr.Length; i++)
        {
            if (negArr[i] < 0)
            {
                hasNegative = true;
                break;
            }
        }
        TestTracker.Record("array.ContainsNegative", hasNegative);

        // Double all elements
        var doubleArr = new int[] { 1, 2, 3 };
        for (int i = 0; i < doubleArr.Length; i++)
        {
            doubleArr[i] *= 2;
        }
        TestTracker.Record("array.DoubleAll", doubleArr[0] == 2 && doubleArr[1] == 4 && doubleArr[2] == 6);

        // Swap first and last
        var swapArr = new int[] { 1, 2, 3, 4, 5 };
        int temp = swapArr[0];
        swapArr[0] = swapArr[4];
        swapArr[4] = temp;
        TestTracker.Record("array.SwapFirstLast", swapArr[0] == 5 && swapArr[4] == 1);

        // Index of max
        var idxMaxArr = new int[] { 3, 7, 2, 9, 5 };
        int maxIdx = 0;
        for (int i = 1; i < idxMaxArr.Length; i++)
        {
            if (idxMaxArr[i] > idxMaxArr[maxIdx])
                maxIdx = i;
        }
        TestTracker.Record("array.IndexOfMax", maxIdx == 3);
    }

    #endregion

    #region Additional Array Tests

    private static void TestAdditionalArrayOps()
    {
        // Multi-dimensional access pattern (using 1D arrays)
        var arr2d = new int[9]; // 3x3 matrix in row-major
        for (int i = 0; i < 9; i++)
            arr2d[i] = i + 1;
        TestTracker.Record("array.Matrix.Set", arr2d[0] == 1 && arr2d[8] == 9);
        TestTracker.Record("array.Matrix.Middle", arr2d[4] == 5); // [1,1] = index 4

        // Large array operations
        var largeArr = new int[1000];
        for (int i = 0; i < largeArr.Length; i++)
            largeArr[i] = i;
        TestTracker.Record("array.Large.First", largeArr[0] == 0);
        TestTracker.Record("array.Large.Middle", largeArr[500] == 500);
        TestTracker.Record("array.Large.Last", largeArr[999] == 999);

        // Long array operations
        var longArr2 = new long[5];
        longArr2[0] = 0L;
        longArr2[1] = 1L;
        longArr2[2] = long.MaxValue;
        longArr2[3] = long.MinValue;
        longArr2[4] = 0x123456789ABCDEF0L;
        TestTracker.Record("array.Long.Zero", longArr2[0] == 0L);
        TestTracker.Record("array.Long.One", longArr2[1] == 1L);
        TestTracker.Record("array.Long.Max", longArr2[2] == long.MaxValue);
        TestTracker.Record("array.Long.Min", longArr2[3] == long.MinValue);
        TestTracker.Record("array.Long.Pattern", longArr2[4] == 0x123456789ABCDEF0L);

        // Byte array operations
        var byteArr2 = new byte[5];
        byteArr2[0] = 0;
        byteArr2[1] = 1;
        byteArr2[2] = 127;
        byteArr2[3] = 128;
        byteArr2[4] = 255;
        TestTracker.Record("array.Byte.Zero", byteArr2[0] == 0);
        TestTracker.Record("array.Byte.One", byteArr2[1] == 1);
        TestTracker.Record("array.Byte.Mid", byteArr2[2] == 127);
        TestTracker.Record("array.Byte.High", byteArr2[3] == 128);
        TestTracker.Record("array.Byte.Max", byteArr2[4] == 255);

        // Float array operations
        var floatArr2 = new float[5];
        floatArr2[0] = 0.0f;
        floatArr2[1] = 1.0f;
        floatArr2[2] = -1.0f;
        floatArr2[3] = 3.14f;
        floatArr2[4] = float.MaxValue;
        TestTracker.Record("array.Float.Zero", floatArr2[0] == 0.0f);
        TestTracker.Record("array.Float.One", floatArr2[1] == 1.0f);
        TestTracker.Record("array.Float.NegOne", floatArr2[2] == -1.0f);
        TestTracker.Record("array.Float.Pi", floatArr2[3] == 3.14f);

        // Double array operations
        var doubleArr2 = new double[5];
        doubleArr2[0] = 0.0;
        doubleArr2[1] = 1.0;
        doubleArr2[2] = -1.0;
        doubleArr2[3] = 3.14159265358979;
        doubleArr2[4] = double.MaxValue;
        TestTracker.Record("array.Double.Zero", doubleArr2[0] == 0.0);
        TestTracker.Record("array.Double.One", doubleArr2[1] == 1.0);
        TestTracker.Record("array.Double.NegOne", doubleArr2[2] == -1.0);
        TestTracker.Record("array.Double.Pi", doubleArr2[3] == 3.14159265358979);

        // Boolean array operations
        var boolArr2 = new bool[4];
        boolArr2[0] = true;
        boolArr2[1] = false;
        boolArr2[2] = true;
        boolArr2[3] = false;
        TestTracker.Record("array.Bool.First", boolArr2[0] == true);
        TestTracker.Record("array.Bool.Second", boolArr2[1] == false);
        TestTracker.Record("array.Bool.Third", boolArr2[2] == true);
        TestTracker.Record("array.Bool.Fourth", boolArr2[3] == false);

        // Char array operations
        var charArr2 = new char[5];
        charArr2[0] = 'A';
        charArr2[1] = 'B';
        charArr2[2] = 'Z';
        charArr2[3] = '0';
        charArr2[4] = '\0';
        TestTracker.Record("array.Char.A", charArr2[0] == 'A');
        TestTracker.Record("array.Char.B", charArr2[1] == 'B');
        TestTracker.Record("array.Char.Z", charArr2[2] == 'Z');
        TestTracker.Record("array.Char.Digit", charArr2[3] == '0');
        TestTracker.Record("array.Char.Null", charArr2[4] == '\0');
    }

    #endregion
}
