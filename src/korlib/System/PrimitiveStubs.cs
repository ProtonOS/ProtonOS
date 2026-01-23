// PrimitiveStubs.cs
// Provides IL implementations of primitive type methods for JIT compilation.
// These methods are excluded from the AOT kernel build but included in korlib.dll
// for JIT-compiled code that references them.
//
// NOTE: This file only compiles when KORLIB_IL is defined (dotnet build of korlib).

#if KORLIB_IL

namespace System
{
    // Single (float) primitive type methods
    public readonly struct Single
    {
        public const float NaN = 0.0f / 0.0f;
        public const float PositiveInfinity = 1.0f / 0.0f;
        public const float NegativeInfinity = -1.0f / 0.0f;

        public static bool IsNaN(float f) => f != f;
        public static bool IsInfinity(float f) => f == PositiveInfinity || f == NegativeInfinity;
        public static bool IsPositiveInfinity(float f) => f == PositiveInfinity;
        public static bool IsNegativeInfinity(float f) => f == NegativeInfinity;
        public static bool IsFinite(float f) => !IsNaN(f) && !IsInfinity(f);
    }

    // Double primitive type methods
    public readonly struct Double
    {
        public const double NaN = 0.0 / 0.0;
        public const double PositiveInfinity = 1.0 / 0.0;
        public const double NegativeInfinity = -1.0 / 0.0;

        public static bool IsNaN(double d) => d != d;
        public static bool IsInfinity(double d) => d == PositiveInfinity || d == NegativeInfinity;
        public static bool IsPositiveInfinity(double d) => d == PositiveInfinity;
        public static bool IsNegativeInfinity(double d) => d == NegativeInfinity;
        public static bool IsFinite(double d) => !IsNaN(d) && !IsInfinity(d);
    }

    // Math class stubs for basic operations
    public static class Math
    {
        public static int Abs(int value) => value < 0 ? -value : value;
        public static long Abs(long value) => value < 0 ? -value : value;
        public static float Abs(float value) => value < 0 ? -value : value;
        public static double Abs(double value) => value < 0 ? -value : value;

        public static int Max(int val1, int val2) => val1 > val2 ? val1 : val2;
        public static long Max(long val1, long val2) => val1 > val2 ? val1 : val2;
        public static float Max(float val1, float val2) => val1 > val2 ? val1 : val2;
        public static double Max(double val1, double val2) => val1 > val2 ? val1 : val2;

        public static int Min(int val1, int val2) => val1 < val2 ? val1 : val2;
        public static long Min(long val1, long val2) => val1 < val2 ? val1 : val2;
        public static float Min(float val1, float val2) => val1 < val2 ? val1 : val2;
        public static double Min(double val1, double val2) => val1 < val2 ? val1 : val2;
    }
}

#endif
