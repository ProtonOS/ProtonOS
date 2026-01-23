// JITTest - List<T> Tests
// Tests System.Collections.Generic.List<T> operations

using System.Collections.Generic;

namespace JITTest;

/// <summary>
/// List tests - verifies List<T> operations.
/// Tests Add, Remove, IndexOf, Contains, Clear, Sort, etc.
/// </summary>
public static class ListTests
{
    public static void RunAll()
    {
        TestListAddAndCount();
        TestListIndexer();
        TestListContains();
        TestListRemove();
        TestListClear();
        TestListIndexOf();
        TestListInsert();
        TestListForeach();
        TestListSort();
        TestListCopyTo();
        TestListAddRange();
        TestListToArray();
    }

    private static void TestListAddAndCount()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        TestTracker.Record("list.AddAndCount", list.Count == 3);
    }

    private static void TestListIndexer()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        bool read = list[0] == 10 && list[1] == 20 && list[2] == 30;
        list[1] = 25;
        bool write = list[1] == 25;
        TestTracker.Record("list.Indexer", read && write);
    }

    private static void TestListContains()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        bool hasItem = list.Contains(20);
        bool noItem = !list.Contains(99);
        TestTracker.Record("list.Contains", hasItem && noItem);
    }

    private static void TestListRemove()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        bool removed = list.Remove(20);
        bool countOk = list.Count == 2;
        bool valuesOk = list[0] == 10 && list[1] == 30;
        TestTracker.Record("list.Remove", removed && countOk && valuesOk);
    }

    private static void TestListClear()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Clear();
        TestTracker.Record("list.Clear", list.Count == 0);
    }

    private static void TestListIndexOf()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        list.Add(20);  // duplicate
        bool foundFirst = list.IndexOf(20) == 1;
        bool notFound = list.IndexOf(99) == -1;
        TestTracker.Record("list.IndexOf", foundFirst && notFound);
    }

    private static void TestListInsert()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(30);
        list.Insert(1, 20);
        bool countOk = list.Count == 3;
        bool valuesOk = list[0] == 10 && list[1] == 20 && list[2] == 30;
        TestTracker.Record("list.Insert", countOk && valuesOk);
    }

    private static void TestListForeach()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        var enumerator = list.GetEnumerator();
        bool moved = enumerator.MoveNext();
        int val = enumerator.Current;
        TestTracker.Record("list.Foreach", moved && val == 10);
    }

    private static void TestListSort()
    {
        var list = new List<int>();
        list.Add(30);
        list.Add(10);
        list.Add(20);
        list.Add(5);
        list.Add(15);
        list.Sort();
        bool sorted = list[0] == 5 && list[1] == 10 && list[2] == 15 &&
                      list[3] == 20 && list[4] == 30;
        TestTracker.Record("list.Sort", sorted);
    }

    private static void TestListCopyTo()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        int[] array = new int[5];
        list.CopyTo(array, 1);
        bool ok = array[0] == 0 && array[1] == 10 && array[2] == 20 &&
                  array[3] == 30 && array[4] == 0;
        TestTracker.Record("list.CopyTo", ok);
    }

    private static void TestListAddRange()
    {
        var list = new List<int>();
        list.Add(10);
        int[] moreItems = { 20, 30, 40 };
        list.AddRange(moreItems);
        bool ok = list.Count == 4 && list[0] == 10 && list[1] == 20 &&
                  list[2] == 30 && list[3] == 40;
        TestTracker.Record("list.AddRange", ok);
    }

    private static void TestListToArray()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        int[] array = list.ToArray();
        bool ok = array.Length == 3 && array[0] == 10 && array[1] == 20 && array[2] == 30;
        TestTracker.Record("list.ToArray", ok);
    }
}
