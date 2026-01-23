// JITTest - Disposable Tests
// Tests using statement and IDisposable pattern

namespace JITTest;

/// <summary>
/// Disposable tests - verifies using statement and IDisposable pattern.
/// Tests dispose called at block end, dispose on exception, nested using.
/// </summary>
public static class DisposableTests
{
    public static void RunAll()
    {
        TestUsingStatement();
        TestUsingWithException();
        TestNestedUsing();
        TestUsingDeclaration();
        TestMultipleDispose();
    }

    private static void TestUsingStatement()
    {
        SimpleDisposable.DisposeCount = 0;
        using (var d = new SimpleDisposable())
        {
            // Inside using block, not disposed yet
            bool notDisposed = !d.IsDisposed;
            TestTracker.Record("disposable.UsingNotDisposedInside", notDisposed);
        }
        // After using block, should be disposed
        TestTracker.Record("disposable.UsingDisposedAfter", SimpleDisposable.DisposeCount == 1);
    }

    private static void TestUsingWithException()
    {
        SimpleDisposable.DisposeCount = 0;
        bool exceptionCaught = false;
        try
        {
            using (var d = new SimpleDisposable())
            {
                throw new Exception("Test exception");
            }
        }
        catch (Exception)
        {
            exceptionCaught = true;
        }
        // Exception caught, but Dispose should have been called
        TestTracker.Record("disposable.UsingWithException",
            exceptionCaught && SimpleDisposable.DisposeCount == 1);
    }

    private static void TestNestedUsing()
    {
        SimpleDisposable.DisposeCount = 0;
        using (var outer = new SimpleDisposable())
        {
            using (var inner = new SimpleDisposable())
            {
                // Both not disposed yet
                bool bothNotDisposed = !outer.IsDisposed && !inner.IsDisposed;
                TestTracker.Record("disposable.NestedNotDisposed", bothNotDisposed);
            }
            // Inner should be disposed now
            TestTracker.Record("disposable.NestedInnerDisposed", SimpleDisposable.DisposeCount == 1);
        }
        // Both should be disposed
        TestTracker.Record("disposable.NestedBothDisposed", SimpleDisposable.DisposeCount == 2);
    }

    private static void TestUsingDeclaration()
    {
        SimpleDisposable.DisposeCount = 0;
        UsingDeclarationHelper();
        // After method returns, should be disposed
        TestTracker.Record("disposable.UsingDeclaration", SimpleDisposable.DisposeCount == 1);
    }

    private static void UsingDeclarationHelper()
    {
        using var d = new SimpleDisposable();
        // d will be disposed when method exits
    }

    private static void TestMultipleDispose()
    {
        SimpleDisposable.DisposeCount = 0;
        // Sequential uses - not nested
        using (var d1 = new SimpleDisposable())
        {
            // d1 active
        }
        // d1 disposed, count should be 1
        using (var d2 = new SimpleDisposable())
        {
            // d2 active
        }
        // d2 disposed, count should be 2
        using (var d3 = new SimpleDisposable())
        {
            // d3 active
        }
        // d3 disposed, count should be 3
        int count = SimpleDisposable.DisposeCount;
        TestTracker.Record("disposable.MultipleDispose", count == 3);
    }
}

// Supporting types
public class SimpleDisposable : IDisposable
{
    public static int DisposeCount;
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            DisposeCount++;
        }
    }
}
