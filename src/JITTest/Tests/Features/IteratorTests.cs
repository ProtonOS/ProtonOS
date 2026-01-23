// JITTest - Iterator Tests
// Tests custom IEnumerable implementations

using System.Collections;

namespace JITTest;

/// <summary>
/// Simple range that implements IEnumerable.
/// </summary>
public class IntRange : IEnumerable
{
    private readonly int _start;
    private readonly int _count;

    public IntRange(int start, int count)
    {
        _start = start;
        _count = count;
    }

    public IEnumerator GetEnumerator()
    {
        return new IntRangeEnumerator(_start, _count);
    }

    private class IntRangeEnumerator : IEnumerator
    {
        private readonly int _start;
        private readonly int _count;
        private int _index = -1;

        public IntRangeEnumerator(int start, int count)
        {
            _start = start;
            _count = count;
        }

        public object? Current => _start + _index;

        public bool MoveNext()
        {
            _index++;
            return _index < _count;
        }

        public void Reset()
        {
            _index = -1;
        }
    }
}

/// <summary>
/// Iterator tests - verifies custom IEnumerable implementations.
/// Tests foreach over custom enumerables.
/// </summary>
public static class IteratorTests
{
    public static void RunAll()
    {
        TestForeachCustomRange();
        TestForeachCustomCount();
        TestForeachCustomEmpty();
        TestForeachCustomBreak();
    }

    private static void TestForeachCustomRange()
    {
        var range = new IntRange(1, 5);  // 1, 2, 3, 4, 5
        int sum = 0;
        foreach (object? item in range)
        {
            if (item != null)
                sum += (int)item;
        }
        TestTracker.Record("iterator.CustomRange", sum == 15);
    }

    private static void TestForeachCustomCount()
    {
        var range = new IntRange(10, 3);  // 10, 11, 12
        int count = 0;
        foreach (object? item in range)
        {
            count++;
        }
        TestTracker.Record("iterator.CustomCount", count == 3);
    }

    private static void TestForeachCustomEmpty()
    {
        var range = new IntRange(0, 0);  // empty
        int sum = 0;
        foreach (object? item in range)
        {
            sum += 100;  // should never execute
        }
        TestTracker.Record("iterator.CustomEmpty", sum == 0);
    }

    private static void TestForeachCustomBreak()
    {
        var range = new IntRange(1, 10);  // 1 through 10
        int sum = 0;
        foreach (object? item in range)
        {
            if (item != null)
            {
                int val = (int)item;
                if (val > 3)
                    break;
                sum += val;
            }
        }
        TestTracker.Record("iterator.CustomBreak", sum == 6);
    }
}
