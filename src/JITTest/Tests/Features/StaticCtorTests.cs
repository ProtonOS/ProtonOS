// JITTest - Static Constructor Tests
// Tests type initializer (.cctor) invocation

namespace JITTest;

// Helper classes with static constructors
public static class StaticCtorHelper1
{
    public static int InitializedValue;

    static StaticCtorHelper1()
    {
        InitializedValue = 42;
    }
}

public static class StaticCtorHelper2
{
    public static int Counter;
    public static int FirstAccess;

    static StaticCtorHelper2()
    {
        Counter = Counter + 1;  // Should only run once
        FirstAccess = 100;
    }
}

public static class StaticCtorHelper3
{
    public static int Value;

    static StaticCtorHelper3()
    {
        // Access StaticCtorHelper1.InitializedValue which triggers its cctor first
        Value = StaticCtorHelper1.InitializedValue + 8;  // Should be 50
    }
}

// Helper class with field initializer only (has beforefieldinit attribute)
public static class BeforeFieldInitHelper
{
    public static int Value = 42;
    public static int Computed = 10 + 32;
}

// Helper class WITHOUT beforefieldinit (has explicit static constructor)
public static class NoBeforeFieldInitHelper
{
    public static int Value;
    public static int AccessCount;

    static NoBeforeFieldInitHelper()
    {
        Value = 42;
        AccessCount = 1;
    }
}

// Circular initialization helpers
public static class CircularA
{
    public static int ValueA;
    public static int FromB;

    static CircularA()
    {
        ValueA = 10;
        FromB = CircularB.ValueB;
    }
}

public static class CircularB
{
    public static int ValueB;
    public static int FromA;

    static CircularB()
    {
        ValueB = 20;
        FromA = CircularA.ValueA;
    }
}

/// <summary>
/// Static constructor tests - verifies type initializer (.cctor) invocation.
/// Tests cctor runs before first access, runs only once, handles dependencies.
/// </summary>
public static class StaticCtorTests
{
    public static void RunAll()
    {
        TestStaticCtorInitializesField();
        TestStaticCtorRunsOnce();
        TestStaticCtorOnWrite();
        TestStaticCtorWithDependency();
        TestBeforeFieldInitValue();
        TestBeforeFieldInitComputed();
        TestNoBeforeFieldInit();
        TestNoBeforeFieldInitRunsOnce();
        TestCircularTwoWay();
    }

    private static void TestStaticCtorInitializesField()
    {
        // Reading InitializedValue should trigger StaticCtorHelper1's static constructor
        int value = StaticCtorHelper1.InitializedValue;
        TestTracker.Record("staticctor.InitializesField", value == 42);
    }

    private static void TestStaticCtorRunsOnce()
    {
        // First access triggers cctor which sets Counter to 1
        int first = StaticCtorHelper2.Counter;
        // Second access should NOT re-run cctor
        int second = StaticCtorHelper2.Counter;
        // Third access
        int third = StaticCtorHelper2.Counter;
        TestTracker.Record("staticctor.RunsOnce", first == 1 && second == 1 && third == 1);
    }

    private static void TestStaticCtorOnWrite()
    {
        // Write to Counter - should trigger cctor first
        StaticCtorHelper2.Counter = StaticCtorHelper2.Counter + 10;
        // FirstAccess should be 100 (set by cctor)
        int firstAccess = StaticCtorHelper2.FirstAccess;
        TestTracker.Record("staticctor.OnWrite", firstAccess == 100);
    }

    private static void TestStaticCtorWithDependency()
    {
        // Helper3's cctor accesses Helper1.InitializedValue (triggers Helper1's cctor)
        // Then computes Value = 42 + 8 = 50
        int value = StaticCtorHelper3.Value;
        TestTracker.Record("staticctor.WithDependency", value == 50);
    }

    private static void TestBeforeFieldInitValue()
    {
        // BeforeFieldInitHelper has field initializers only
        int value = BeforeFieldInitHelper.Value;
        TestTracker.Record("staticctor.BeforeFieldInitValue", value == 42);
    }

    private static void TestBeforeFieldInitComputed()
    {
        // Computed = 10 + 32 = 42
        int computed = BeforeFieldInitHelper.Computed;
        TestTracker.Record("staticctor.BeforeFieldInitComputed", computed == 42);
    }

    private static void TestNoBeforeFieldInit()
    {
        // NoBeforeFieldInitHelper has explicit cctor
        int value = NoBeforeFieldInitHelper.Value;
        TestTracker.Record("staticctor.NoBeforeFieldInit", value == 42);
    }

    private static void TestNoBeforeFieldInitRunsOnce()
    {
        int a = NoBeforeFieldInitHelper.AccessCount;
        int b = NoBeforeFieldInitHelper.AccessCount;
        TestTracker.Record("staticctor.NoBeforeFieldInitRunsOnce", a == 1 && b == 1);
    }

    private static void TestCircularTwoWay()
    {
        // Access CircularA first - triggers A's cctor which accesses B
        int valueA = CircularA.ValueA;
        int valueB = CircularB.ValueB;
        // A.ValueA = 10, B.ValueB = 20
        TestTracker.Record("staticctor.CircularTwoWay", valueA == 10 && valueB == 20);
    }
}
