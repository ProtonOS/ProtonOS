// JITTest - StringBuilder Tests
// Tests System.Text.StringBuilder operations

using System.Text;

namespace JITTest;

/// <summary>
/// StringBuilder tests - verifies StringBuilder operations.
/// Tests Append, Insert, Remove, Replace, Clear, etc.
/// </summary>
public static class StringBuilderTests
{
    public static void RunAll()
    {
        TestStringBuilderAppend();
        TestStringBuilderAppendLine();
        TestStringBuilderInsert();
        TestStringBuilderRemove();
        TestStringBuilderReplace();
        TestStringBuilderClear();
        TestStringBuilderLength();
        TestStringBuilderCapacity();
        TestStringBuilderIndexer();
        TestStringBuilderChaining();
    }

    private static void TestStringBuilderAppend()
    {
        var sb = new StringBuilder();
        sb.Append("Hello");
        sb.Append(' ');
        sb.Append("World");
        string result = sb.ToString();
        TestTracker.Record("stringbuilder.Append", result == "Hello World");
    }

    private static void TestStringBuilderAppendLine()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Line1");
        sb.Append("Line2");
        string result = sb.ToString();
        // "Line1\nLine2" = 5 + 1 + 5 = 11
        TestTracker.Record("stringbuilder.AppendLine", result.Length == 11);
    }

    private static void TestStringBuilderInsert()
    {
        var sb = new StringBuilder("HelloWorld");
        sb.Insert(5, " ");
        string result = sb.ToString();
        TestTracker.Record("stringbuilder.Insert", result == "Hello World");
    }

    private static void TestStringBuilderRemove()
    {
        var sb = new StringBuilder("Hello World");
        sb.Remove(5, 1);  // Remove the space
        string result = sb.ToString();
        TestTracker.Record("stringbuilder.Remove", result == "HelloWorld");
    }

    private static void TestStringBuilderReplace()
    {
        var sb = new StringBuilder("Hello World");
        sb.Replace("World", "Universe");
        string result = sb.ToString();
        TestTracker.Record("stringbuilder.Replace", result == "Hello Universe");
    }

    private static void TestStringBuilderClear()
    {
        var sb = new StringBuilder("Hello World");
        sb.Clear();
        bool lengthOk = sb.Length == 0;
        bool emptyOk = sb.ToString() == "";
        TestTracker.Record("stringbuilder.Clear", lengthOk && emptyOk);
    }

    private static void TestStringBuilderLength()
    {
        var sb = new StringBuilder("Hello");
        bool len5 = sb.Length == 5;
        sb.Length = 3;  // truncate
        bool truncate = sb.ToString() == "Hel";
        sb.Length = 5;  // extend
        bool extend = sb.Length == 5;
        TestTracker.Record("stringbuilder.Length", len5 && truncate && extend);
    }

    private static void TestStringBuilderCapacity()
    {
        var sb = new StringBuilder(32);
        bool cap32 = sb.Capacity >= 32;
        sb.EnsureCapacity(100);
        bool cap100 = sb.Capacity >= 100;
        TestTracker.Record("stringbuilder.Capacity", cap32 && cap100);
    }

    private static void TestStringBuilderIndexer()
    {
        var sb = new StringBuilder("Hello");
        bool readOk = sb[0] == 'H' && sb[4] == 'o';
        sb[0] = 'J';
        bool writeOk = sb.ToString() == "Jello";
        TestTracker.Record("stringbuilder.Indexer", readOk && writeOk);
    }

    private static void TestStringBuilderChaining()
    {
        var result = new StringBuilder()
            .Append("Hello")
            .Append(' ')
            .Append("World")
            .Replace("World", "Everyone")
            .ToString();
        TestTracker.Record("stringbuilder.Chaining", result == "Hello Everyone");
    }
}
