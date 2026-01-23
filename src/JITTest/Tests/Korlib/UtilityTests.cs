// JITTest - Utility Tests
// Tests BitConverter, TimeSpan, DateTime, ArraySegment, Guid, Queue, Stack

using System;
using System.Collections.Generic;

namespace JITTest;

/// <summary>
/// Utility tests - verifies korlib utility classes.
/// Tests BitConverter, TimeSpan, DateTime, ArraySegment, Queue, Stack.
/// </summary>
public static class UtilityTests
{
    public static void RunAll()
    {
        // BitConverter
        TestBitConverterInt32();
        TestBitConverterInt64();
        TestBitConverterRoundtrip();

        // TimeSpan
        TestTimeSpanBasic();
        TestTimeSpanArithmetic();
        TestTimeSpanCompare();

        // DateTime
        TestDateTimeBasic();
        TestDateTimeComponents();
        TestDateTimeLeapYear();

        // ArraySegment
        TestArraySegmentBasic();
        TestArraySegmentCopyTo();
        TestArraySegmentToArray();

        // Guid
        TestGuidFromBytes();
        TestGuidEquality();

        // Queue
        TestQueueBasic();
        TestQueueForeach();

        // Stack
        TestStackBasic();
        TestStackForeach();
    }

    // BitConverter Tests
    private static void TestBitConverterInt32()
    {
        int value = 0x12345678;
        byte[] bytes = BitConverter.GetBytes(value);
        bool lenOk = bytes.Length == 4;
        bool bytesOk = bytes[0] == 0x78 && bytes[1] == 0x56 &&
                       bytes[2] == 0x34 && bytes[3] == 0x12;
        int result = BitConverter.ToInt32(bytes, 0);
        TestTracker.Record("utility.BitConverterInt32", lenOk && bytesOk && result == value);
    }

    private static void TestBitConverterInt64()
    {
        long value = 0x123456789ABCDEF0;
        byte[] bytes = BitConverter.GetBytes(value);
        bool lenOk = bytes.Length == 8;
        long result = BitConverter.ToInt64(bytes, 0);
        TestTracker.Record("utility.BitConverterInt64", lenOk && result == value);
    }

    private static void TestBitConverterRoundtrip()
    {
        short s = -1234;
        bool shortOk = BitConverter.ToInt16(BitConverter.GetBytes(s), 0) == s;
        uint u = 0xDEADBEEF;
        bool uintOk = BitConverter.ToUInt32(BitConverter.GetBytes(u), 0) == u;
        bool boolOk = BitConverter.ToBoolean(BitConverter.GetBytes(true), 0) == true &&
                      BitConverter.ToBoolean(BitConverter.GetBytes(false), 0) == false;
        TestTracker.Record("utility.BitConverterRoundtrip", shortOk && uintOk && boolOk);
    }

    // TimeSpan Tests
    private static void TestTimeSpanBasic()
    {
        var ts = new TimeSpan(1, 2, 30, 45);
        bool ok = ts.Days == 1 && ts.Hours == 2 && ts.Minutes == 30 && ts.Seconds == 45;
        TestTracker.Record("utility.TimeSpanBasic", ok);
    }

    private static void TestTimeSpanArithmetic()
    {
        var ts1 = new TimeSpan(1, 0, 0);  // 1 hour
        var ts2 = new TimeSpan(0, 30, 0); // 30 minutes
        var sum = ts1 + ts2;
        bool sumOk = sum.Hours == 1 && sum.Minutes == 30;
        var diff = ts1 - ts2;
        bool diffOk = diff.Hours == 0 && diff.Minutes == 30;
        TestTracker.Record("utility.TimeSpanArithmetic", sumOk && diffOk);
    }

    private static void TestTimeSpanCompare()
    {
        var ts1 = new TimeSpan(0, 1, 0);
        var ts2 = new TimeSpan(0, 1, 0);
        var ts3 = new TimeSpan(0, 2, 0);
        bool eqOk = ts1 == ts2;
        bool ltOk = ts1 < ts3;
        bool gtOk = ts3 > ts1;
        TestTracker.Record("utility.TimeSpanCompare", eqOk && ltOk && gtOk);
    }

    // DateTime Tests
    private static void TestDateTimeBasic()
    {
        var dt = new DateTime(2024, 12, 25);
        bool ok = dt.Year == 2024 && dt.Month == 12 && dt.Day == 25;
        TestTracker.Record("utility.DateTimeBasic", ok);
    }

    private static void TestDateTimeComponents()
    {
        var dt = new DateTime(2024, 6, 15, 14, 30, 45, 123);
        bool ok = dt.Year == 2024 && dt.Month == 6 && dt.Day == 15 &&
                  dt.Hour == 14 && dt.Minute == 30 && dt.Second == 45 &&
                  dt.Millisecond == 123;
        TestTracker.Record("utility.DateTimeComponents", ok);
    }

    private static void TestDateTimeLeapYear()
    {
        bool leap2024 = DateTime.IsLeapYear(2024);
        bool noLeap2023 = !DateTime.IsLeapYear(2023);
        bool leap2000 = DateTime.IsLeapYear(2000);
        bool noLeap1900 = !DateTime.IsLeapYear(1900);
        bool febLeap = DateTime.DaysInMonth(2024, 2) == 29;
        bool febNoLeap = DateTime.DaysInMonth(2023, 2) == 28;
        TestTracker.Record("utility.DateTimeLeapYear",
            leap2024 && noLeap2023 && leap2000 && noLeap1900 && febLeap && febNoLeap);
    }

    // ArraySegment Tests
    private static void TestArraySegmentBasic()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        var seg = new ArraySegment<int>(arr, 1, 3);
        bool countOk = seg.Count == 3;
        bool offsetOk = seg.Offset == 1;
        bool valuesOk = seg[0] == 20 && seg[1] == 30 && seg[2] == 40;
        seg[1] = 35;
        bool setOk = seg[1] == 35 && arr[2] == 35;
        TestTracker.Record("utility.ArraySegmentBasic", countOk && offsetOk && valuesOk && setOk);
    }

    private static void TestArraySegmentCopyTo()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        var seg = new ArraySegment<int>(arr, 1, 3);
        int[] dest = new int[5];
        seg.CopyTo(dest, 1);
        bool ok = dest[0] == 0 && dest[1] == 20 && dest[2] == 30 &&
                  dest[3] == 40 && dest[4] == 0;
        TestTracker.Record("utility.ArraySegmentCopyTo", ok);
    }

    private static void TestArraySegmentToArray()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        var seg = new ArraySegment<int>(arr, 2, 2);
        int[] copy = seg.ToArray();
        bool lenOk = copy.Length == 2;
        bool valOk = copy[0] == 30 && copy[1] == 40;
        copy[0] = 99;
        bool copyOk = arr[2] == 30;  // Original unchanged
        TestTracker.Record("utility.ArraySegmentToArray", lenOk && valOk && copyOk);
    }

    // Guid Tests
    private static void TestGuidFromBytes()
    {
        byte[] bytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                    0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
        var guid = new Guid(bytes);
        byte[] result = guid.ToByteArray();
        bool ok = true;
        for (int i = 0; i < 16; i++)
        {
            if (result[i] != bytes[i]) ok = false;
        }
        TestTracker.Record("utility.GuidFromBytes", ok);
    }

    private static void TestGuidEquality()
    {
        byte[] bytes1 = new byte[] { 0x78, 0x56, 0x34, 0x12, 0x34, 0x12, 0x78, 0x56,
                                     0x9A, 0xBC, 0xDE, 0xF0, 0x12, 0x34, 0x56, 0x78 };
        byte[] bytes2 = new byte[] { 0x78, 0x56, 0x34, 0x12, 0x34, 0x12, 0x78, 0x56,
                                     0x9A, 0xBC, 0xDE, 0xF0, 0x12, 0x34, 0x56, 0x78 };
        byte[] bytes3 = new byte[] { 0x21, 0x43, 0x65, 0x87, 0x21, 0x43, 0x65, 0x87,
                                     0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };
        var guid1 = new Guid(bytes1);
        var guid2 = new Guid(bytes2);
        var guid3 = new Guid(bytes3);
        bool eqOk = guid1 == guid2;
        bool neqOk = guid1 != guid3;
        TestTracker.Record("utility.GuidEquality", eqOk && neqOk);
    }

    // Queue Tests
    private static void TestQueueBasic()
    {
        var queue = new Queue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        bool countOk = queue.Count == 3;
        bool deq1 = queue.Dequeue() == 1;
        bool deq2 = queue.Dequeue() == 2;
        bool afterCount = queue.Count == 1;
        bool peekOk = queue.Peek() == 3 && queue.Count == 1;
        TestTracker.Record("utility.QueueBasic", countOk && deq1 && deq2 && afterCount && peekOk);
    }

    private static void TestQueueForeach()
    {
        var queue = new Queue<int>();
        queue.Enqueue(10);
        queue.Enqueue(20);
        queue.Enqueue(30);
        int sum = 0;
        foreach (int item in queue)
        {
            sum += item;
        }
        TestTracker.Record("utility.QueueForeach", sum == 60);
    }

    // Stack Tests
    private static void TestStackBasic()
    {
        var stack = new Stack<int>();
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        bool countOk = stack.Count == 3;
        bool pop1 = stack.Pop() == 3;  // LIFO
        bool pop2 = stack.Pop() == 2;
        bool afterCount = stack.Count == 1;
        bool peekOk = stack.Peek() == 1 && stack.Count == 1;
        TestTracker.Record("utility.StackBasic", countOk && pop1 && pop2 && afterCount && peekOk);
    }

    private static void TestStackForeach()
    {
        var stack = new Stack<int>();
        stack.Push(10);
        stack.Push(20);
        stack.Push(30);
        int sum = 0;
        foreach (int item in stack)
        {
            sum += item;
        }
        TestTracker.Record("utility.StackForeach", sum == 60);
    }
}
