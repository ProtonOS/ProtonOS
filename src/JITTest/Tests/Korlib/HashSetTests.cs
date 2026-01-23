// JITTest - HashSet<T> Tests
// Tests System.Collections.Generic.HashSet<T> operations

using System.Collections.Generic;

namespace JITTest;

/// <summary>
/// HashSet tests - verifies HashSet<T> operations.
/// Tests Add, Remove, Contains, Clear, UnionWith, IntersectWith, etc.
/// </summary>
public static class HashSetTests
{
    public static void RunAll()
    {
        TestHashSetAddAndCount();
        TestHashSetContains();
        TestHashSetRemove();
        TestHashSetClear();
        TestHashSetDuplicates();
        TestHashSetForeach();
        TestHashSetUnionWith();
        TestHashSetIntersectWith();
        TestHashSetExceptWith();
        TestHashSetOverlaps();
    }

    private static void TestHashSetAddAndCount()
    {
        var set = new HashSet<int>();
        set.Add(10);
        set.Add(20);
        set.Add(30);
        TestTracker.Record("hashset.AddAndCount", set.Count == 3);
    }

    private static void TestHashSetContains()
    {
        var set = new HashSet<int>();
        set.Add(10);
        set.Add(20);
        set.Add(30);
        bool has10 = set.Contains(10);
        bool has20 = set.Contains(20);
        bool no99 = !set.Contains(99);
        TestTracker.Record("hashset.Contains", has10 && has20 && no99);
    }

    private static void TestHashSetRemove()
    {
        var set = new HashSet<int>();
        set.Add(10);
        set.Add(20);
        set.Add(30);
        bool removed = set.Remove(20);
        bool countOk = set.Count == 2;
        bool no20 = !set.Contains(20);
        bool has10 = set.Contains(10);
        TestTracker.Record("hashset.Remove", removed && countOk && no20 && has10);
    }

    private static void TestHashSetClear()
    {
        var set = new HashSet<int>();
        set.Add(10);
        set.Add(20);
        set.Clear();
        TestTracker.Record("hashset.Clear", set.Count == 0);
    }

    private static void TestHashSetDuplicates()
    {
        var set = new HashSet<int>();
        bool added1 = set.Add(10);
        bool added2 = set.Add(10);  // Duplicate
        bool added3 = set.Add(20);
        bool ok = added1 && !added2 && added3 && set.Count == 2;
        TestTracker.Record("hashset.Duplicates", ok);
    }

    private static void TestHashSetForeach()
    {
        var set = new HashSet<int>();
        set.Add(10);
        set.Add(20);
        set.Add(30);
        int sum = 0;
        foreach (int item in set)
        {
            sum += item;
        }
        TestTracker.Record("hashset.Foreach", sum == 60);
    }

    private static void TestHashSetUnionWith()
    {
        var set1 = new HashSet<int>();
        set1.Add(1);
        set1.Add(2);

        var set2 = new HashSet<int>();
        set2.Add(2);
        set2.Add(3);

        set1.UnionWith(set2);
        bool countOk = set1.Count == 3;
        bool has1 = set1.Contains(1);
        bool has2 = set1.Contains(2);
        bool has3 = set1.Contains(3);
        TestTracker.Record("hashset.UnionWith", countOk && has1 && has2 && has3);
    }

    private static void TestHashSetIntersectWith()
    {
        var set1 = new HashSet<int>();
        set1.Add(1);
        set1.Add(2);
        set1.Add(3);

        var set2 = new HashSet<int>();
        set2.Add(2);
        set2.Add(3);
        set2.Add(4);

        set1.IntersectWith(set2);
        bool countOk = set1.Count == 2;
        bool no1 = !set1.Contains(1);
        bool has2 = set1.Contains(2);
        bool has3 = set1.Contains(3);
        TestTracker.Record("hashset.IntersectWith", countOk && no1 && has2 && has3);
    }

    private static void TestHashSetExceptWith()
    {
        var set1 = new HashSet<int>();
        set1.Add(1);
        set1.Add(2);
        set1.Add(3);

        var set2 = new HashSet<int>();
        set2.Add(2);

        set1.ExceptWith(set2);
        bool countOk = set1.Count == 2;
        bool has1 = set1.Contains(1);
        bool no2 = !set1.Contains(2);
        bool has3 = set1.Contains(3);
        TestTracker.Record("hashset.ExceptWith", countOk && has1 && no2 && has3);
    }

    private static void TestHashSetOverlaps()
    {
        var set1 = new HashSet<int>();
        set1.Add(1);
        set1.Add(2);

        var set2 = new HashSet<int>();
        set2.Add(2);
        set2.Add(3);

        var set3 = new HashSet<int>();
        set3.Add(4);
        set3.Add(5);

        bool overlaps = set1.Overlaps(set2);
        bool noOverlap = !set1.Overlaps(set3);
        TestTracker.Record("hashset.Overlaps", overlaps && noOverlap);
    }
}
