// JITTest - Nullable Tests
// Tests Nullable<T> operations and lifted operators

namespace JITTest;

/// <summary>
/// Nullable tests - verifies Nullable<T> operations.
/// Tests HasValue, Value, GetValueOrDefault, boxing/unboxing, lifted operators.
/// </summary>
public static class NullableTests
{
    public static void RunAll()
    {
        TestNullableHasValue();
        TestNullableValue();
        TestNullableNoValue();
        TestNullableGetValueOrDefaultWithValue();
        TestNullableGetValueOrDefaultNoValue();
        TestNullableGetValueOrDefaultCustomWithValue();
        TestNullableGetValueOrDefaultCustomNoValue();
        TestNullableImplicitConversion();
        TestNullableAssignNull();
        TestNullableParameter();
        TestNullableParameterNull();
        TestNullableReturn();
        TestNullableReturnNull();
        TestNullableBoxingWithValue();
        TestNullableBoxingNull();
        TestNullableBoxingNoHasValue();
        TestNullableUnboxFromBoxedInt();
        TestNullableUnboxFromNull();
        TestNullableRoundTrip();
        TestNullableRoundTripNull();
        TestLiftedAddBothValues();
        TestLiftedAddFirstNull();
        TestLiftedAddSecondNull();
        TestLiftedAddBothNull();
        TestLiftedSubtract();
        TestLiftedMultiply();
        TestLiftedDivide();
        TestLiftedEqualsBothSame();
        TestLiftedEqualsBothDifferent();
        TestLiftedEqualsBothNull();
        TestLiftedEqualsOneNull();
    }

    private static void TestNullableHasValue()
    {
        int? x = 42;
        TestTracker.Record("nullable.HasValue", x.HasValue);
    }

    private static void TestNullableValue()
    {
        int? x = 42;
        TestTracker.Record("nullable.Value", x.Value == 42);
    }

    private static void TestNullableNoValue()
    {
        int? x = null;
        TestTracker.Record("nullable.NoValue", !x.HasValue);
    }

    private static void TestNullableGetValueOrDefaultWithValue()
    {
        int? x = 42;
        TestTracker.Record("nullable.GetValueOrDefaultWithValue", x.GetValueOrDefault() == 42);
    }

    private static void TestNullableGetValueOrDefaultNoValue()
    {
        int? x = null;
        TestTracker.Record("nullable.GetValueOrDefaultNoValue", x.GetValueOrDefault() == 0);
    }

    private static void TestNullableGetValueOrDefaultCustomWithValue()
    {
        int? x = 42;
        TestTracker.Record("nullable.GetValueOrDefaultCustomWithValue", x.GetValueOrDefault(99) == 42);
    }

    private static void TestNullableGetValueOrDefaultCustomNoValue()
    {
        int? x = null;
        TestTracker.Record("nullable.GetValueOrDefaultCustomNoValue", x.GetValueOrDefault(99) == 99);
    }

    private static void TestNullableImplicitConversion()
    {
        int? x = 42;
        TestTracker.Record("nullable.ImplicitConversion", x.HasValue && x.Value == 42);
    }

    private static void TestNullableAssignNull()
    {
        int? x = 42;
        x = null;
        TestTracker.Record("nullable.AssignNull", !x.HasValue);
    }

    private static void TestNullableParameter()
    {
        int? x = 42;
        int result = GetNullableValueOrZero(x);
        TestTracker.Record("nullable.Parameter", result == 42);
    }

    private static void TestNullableParameterNull()
    {
        int? x = null;
        TestTracker.Record("nullable.ParameterNull", GetNullableValueOrZero(x) == 0);
    }

    private static int GetNullableValueOrZero(int? value)
    {
        return value.HasValue ? value.Value : 0;
    }

    private static void TestNullableReturn()
    {
        int? result = GetNullableFortyTwo();
        TestTracker.Record("nullable.Return", result.HasValue && result.Value == 42);
    }

    private static void TestNullableReturnNull()
    {
        int? result = GetNullableNull();
        TestTracker.Record("nullable.ReturnNull", !result.HasValue);
    }

    private static int? GetNullableFortyTwo() => 42;
    private static int? GetNullableNull() => null;

    private static void TestNullableBoxingWithValue()
    {
        int? x = 42;
        object boxed = x;
        TestTracker.Record("nullable.BoxingWithValue", boxed != null && (int)boxed == 42);
    }

    private static void TestNullableBoxingNull()
    {
        int? x = null;
        object boxed = x;
        TestTracker.Record("nullable.BoxingNull", boxed == null);
    }

    private static void TestNullableBoxingNoHasValue()
    {
        int? x = new int?();
        object boxed = x;
        TestTracker.Record("nullable.BoxingNoHasValue", boxed == null);
    }

    private static void TestNullableUnboxFromBoxedInt()
    {
        object boxed = (object)42;
        int? result = (int?)boxed;
        TestTracker.Record("nullable.UnboxFromBoxedInt", result.HasValue && result.Value == 42);
    }

    private static void TestNullableUnboxFromNull()
    {
        object boxed = null;
        int? result = (int?)boxed;
        TestTracker.Record("nullable.UnboxFromNull", !result.HasValue);
    }

    private static void TestNullableRoundTrip()
    {
        int? original = 99;
        object boxed = original;
        int? result = (int?)boxed;
        TestTracker.Record("nullable.RoundTrip", result.HasValue && result.Value == 99);
    }

    private static void TestNullableRoundTripNull()
    {
        int? original = null;
        object boxed = original;
        int? result = (int?)boxed;
        TestTracker.Record("nullable.RoundTripNull", !result.HasValue);
    }

    private static void TestLiftedAddBothValues()
    {
        int? a = 10;
        int? b = 32;
        int? result = a + b;
        TestTracker.Record("nullable.LiftedAddBothValues", result.HasValue && result.Value == 42);
    }

    private static void TestLiftedAddFirstNull()
    {
        int? a = null;
        int? b = 32;
        int? result = a + b;
        TestTracker.Record("nullable.LiftedAddFirstNull", !result.HasValue);
    }

    private static void TestLiftedAddSecondNull()
    {
        int? a = 10;
        int? b = null;
        int? result = a + b;
        TestTracker.Record("nullable.LiftedAddSecondNull", !result.HasValue);
    }

    private static void TestLiftedAddBothNull()
    {
        int? a = null;
        int? b = null;
        int? result = a + b;
        TestTracker.Record("nullable.LiftedAddBothNull", !result.HasValue);
    }

    private static void TestLiftedSubtract()
    {
        int? a = 50;
        int? b = 8;
        int? result = a - b;
        TestTracker.Record("nullable.LiftedSubtract", result.HasValue && result.Value == 42);
    }

    private static void TestLiftedMultiply()
    {
        int? a = 6;
        int? b = 7;
        int? result = a * b;
        TestTracker.Record("nullable.LiftedMultiply", result.HasValue && result.Value == 42);
    }

    private static void TestLiftedDivide()
    {
        int? a = 84;
        int? b = 2;
        int? result = a / b;
        TestTracker.Record("nullable.LiftedDivide", result.HasValue && result.Value == 42);
    }

    private static void TestLiftedEqualsBothSame()
    {
        int? a = 42;
        int? b = 42;
        TestTracker.Record("nullable.LiftedEqualsBothSame", a == b);
    }

    private static void TestLiftedEqualsBothDifferent()
    {
        int? a = 42;
        int? b = 99;
        TestTracker.Record("nullable.LiftedEqualsBothDifferent", !(a == b));
    }

    private static void TestLiftedEqualsBothNull()
    {
        int? a = null;
        int? b = null;
        TestTracker.Record("nullable.LiftedEqualsBothNull", a == b);
    }

    private static void TestLiftedEqualsOneNull()
    {
        int? a = 42;
        int? b = null;
        TestTracker.Record("nullable.LiftedEqualsOneNull", !(a == b));
    }
}
