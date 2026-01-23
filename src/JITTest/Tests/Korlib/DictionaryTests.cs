// JITTest - Dictionary<K,V> Tests
// Tests System.Collections.Generic.Dictionary<K,V> operations

using System.Collections.Generic;

namespace JITTest;

/// <summary>
/// Dictionary tests - verifies Dictionary<K,V> operations.
/// Tests Add, Remove, ContainsKey, TryGetValue, Clear, etc.
/// </summary>
public static class DictionaryTests
{
    public static void RunAll()
    {
        TestDictAddAndCount();
        TestDictIndexer();
        TestDictContainsKey();
        TestDictTryGetValue();
        TestDictRemove();
        TestDictClear();
        TestDictUpdate();
        TestDictForeach();
        TestDictKeys();
        TestDictValues();
    }

    private static void TestDictAddAndCount()
    {
        var dict = new Dictionary<string, int>();
        dict.Add("one", 1);
        dict.Add("two", 2);
        dict.Add("three", 3);
        TestTracker.Record("dict.AddAndCount", dict.Count == 3);
    }

    private static void TestDictIndexer()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        dict["two"] = 2;
        bool read = dict["one"] == 1 && dict["two"] == 2;
        dict["one"] = 100;
        bool write = dict["one"] == 100;
        TestTracker.Record("dict.Indexer", read && write);
    }

    private static void TestDictContainsKey()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        dict["two"] = 2;
        bool hasOne = dict.ContainsKey("one");
        bool hasTwo = dict.ContainsKey("two");
        bool noThree = !dict.ContainsKey("three");
        TestTracker.Record("dict.ContainsKey", hasOne && hasTwo && noThree);
    }

    private static void TestDictTryGetValue()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        dict["two"] = 2;
        bool found1 = dict.TryGetValue("one", out int val1) && val1 == 1;
        bool found2 = dict.TryGetValue("two", out int val2) && val2 == 2;
        bool notFound = !dict.TryGetValue("three", out _);
        TestTracker.Record("dict.TryGetValue", found1 && found2 && notFound);
    }

    private static void TestDictRemove()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        dict["two"] = 2;
        dict["three"] = 3;
        bool removed = dict.Remove("two");
        bool countOk = dict.Count == 2;
        bool noTwo = !dict.ContainsKey("two");
        TestTracker.Record("dict.Remove", removed && countOk && noTwo);
    }

    private static void TestDictClear()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        dict["two"] = 2;
        dict.Clear();
        TestTracker.Record("dict.Clear", dict.Count == 0);
    }

    private static void TestDictUpdate()
    {
        var dict = new Dictionary<string, int>();
        dict["key"] = 100;
        dict["key"] = 200;
        bool valueOk = dict["key"] == 200;
        bool countOk = dict.Count == 1;
        TestTracker.Record("dict.Update", valueOk && countOk);
    }

    private static void TestDictForeach()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        var enumerator = dict.GetEnumerator();
        bool hasNext = enumerator.MoveNext();
        TestTracker.Record("dict.Foreach", hasNext);
    }

    private static void TestDictKeys()
    {
        var dict = new Dictionary<int, string>();
        dict[1] = "one";
        dict[2] = "two";
        dict[3] = "three";
        var keys = dict.Keys;
        int count = keys.Count;
        TestTracker.Record("dict.Keys", count == 3);
    }

    private static void TestDictValues()
    {
        var dict = new Dictionary<string, int>();
        dict["a"] = 10;
        dict["b"] = 20;
        dict["c"] = 30;
        var values = dict.Values;
        int count = values.Count;
        TestTracker.Record("dict.Values", count == 3);
    }
}
