// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Runtime;
using System.Runtime.CompilerServices;

namespace System
{
    public static class Math
    {
        internal static int ConvertToInt32Checked(double value)
        {
            Environment.FailFast(null);
            return 0;
        }

        internal static uint ConvertToUInt32Checked(double value)
        {
            Environment.FailFast(null);
            return 0;
        }

        internal static long ConvertToInt64Checked(double value)
        {
            Environment.FailFast(null);
            return 0;
        }

        internal static ulong ConvertToUInt64Checked(double value)
        {
            Environment.FailFast(null);
            return 0;
        }

        internal static int DivInt32(int dividend, int divisor)
        {
            if ((uint)(divisor + 1) <= 1)
            {
                if (divisor == 0)
                {
                    Environment.FailFast(null);
                    return 0;
                }
                else if (divisor == -1)
                {
                    if (dividend == int.MinValue)
                    {
                        Environment.FailFast(null);
                        return 0;
                    }
                    return -dividend;
                }
            }

            return DivInt32Internal(dividend, divisor);
        }

        internal static uint DivUInt32(uint dividend, uint divisor)
        {
            if (divisor == 0)
            {
                Environment.FailFast(null);
                return 0;
            }

            return DivUInt32Internal(dividend, divisor);
        }

        internal static long DivInt64(long dividend, long divisor)
        {
            if ((int)((ulong)divisor >> 32) == (int)(((ulong)(int)divisor) >> 32))
            {
                if ((int)divisor == 0)
                {
                    Environment.FailFast(null);
                    return 0;
                }

                if ((int)divisor == -1)
                {
                    if (dividend == long.MinValue)
                    {
                        Environment.FailFast(null);
                        return 0;
                    }
                    return -dividend;
                }

                if ((int)((ulong)dividend >> 32) == (int)(((ulong)(int)dividend) >> 32))
                {
                    return DivInt32Internal((int)dividend, (int)divisor);
                }
            }

            return DivInt64Internal(dividend, divisor);
        }

        internal static ulong DivUInt64(ulong dividend, ulong divisor)
        {
            if ((int)(divisor >> 32) == 0)
            {
                if ((uint)divisor == 0)
                {
                    Environment.FailFast(null);
                    return 0;
                }

                if ((int)(dividend >> 32) == 0)
                {
                    return DivUInt32Internal((uint)dividend, (uint)divisor);
                }
            }

            return DivUInt64Internal(dividend, divisor);
        }

        internal static int ModInt32(int dividend, int divisor)
        {
            if ((uint)(divisor + 1) <= 1)
            {
                if (divisor == 0)
                {
                    Environment.FailFast(null);
                    return 0;
                }
                else if (divisor == -1)
                {
                    if (dividend == int.MinValue)
                    {
                        Environment.FailFast(null);
                        return 0;
                    }
                    return 0;
                }
            }

            return ModInt32Internal(dividend, divisor);
        }

        internal static uint ModUInt32(uint dividend, uint divisor)
        {
            if (divisor == 0)
            {
                Environment.FailFast(null);
                return 0;
            }

            return ModUInt32Internal(dividend, divisor);
        }

        internal static long ModInt64(long dividend, long divisor)
        {
            if ((int)((ulong)divisor >> 32) == (int)(((ulong)(int)divisor) >> 32))
            {
                if ((int)divisor == 0)
                {
                    Environment.FailFast(null);
                    return 0;
                }

                if ((int)divisor == -1)
                {
                    if (dividend == long.MinValue)
                    {
                        Environment.FailFast(null);
                        return 0;
                    }
                    return 0;
                }

                if ((int)((ulong)dividend >> 32) == (int)(((ulong)(int)dividend) >> 32))
                {
                    return ModInt32Internal((int)dividend, (int)divisor);
                }
            }

            return ModInt64Internal(dividend, divisor);
        }

        internal static ulong ModUInt64(ulong dividend, ulong divisor)
        {
            if ((int)(divisor >> 32) == 0)
            {
                if ((uint)divisor == 0)
                {
                    Environment.FailFast(null);
                    return 0;
                }

                if ((int)(dividend >> 32) == 0)
                {
                    return ModUInt32Internal((uint)dividend, (uint)divisor);
                }
            }

            return ModUInt64Internal(dividend, divisor);
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "DivInt32Internal")]
        private static extern int DivInt32Internal(int dividend, int divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "DivUInt32Internal")]
        private static extern uint DivUInt32Internal(uint dividend, uint divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "DivInt64Internal")]
        private static extern long DivInt64Internal(long dividend, long divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "DivUInt64Internal")]
        private static extern ulong DivUInt64Internal(ulong dividend, ulong divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "ModInt32Internal")]
        private static extern int ModInt32Internal(int dividend, int divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "ModUInt32Internal")]
        private static extern uint ModUInt32Internal(uint dividend, uint divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "ModInt64Internal")]
        private static extern long ModInt64Internal(long dividend, long divisor);

        [MethodImpl(MethodImplOptions.InternalCall)]
        [RuntimeImport("*", "ModUInt64Internal")]
        private static extern ulong ModUInt64Internal(ulong dividend, ulong divisor);

        // ============================================
        // Public Math constants and methods
        // ============================================

        /// <summary>
        /// Represents the ratio of the circumference of a circle to its diameter (π ≈ 3.14159).
        /// </summary>
        public const double PI = 3.1415926535897932;

        /// <summary>
        /// Represents the natural logarithmic base (e ≈ 2.71828).
        /// </summary>
        public const double E = 2.7182818284590452;

        /// <summary>
        /// Represents the number of radians in one turn (τ = 2π).
        /// </summary>
        public const double Tau = 6.2831853071795864;

        // Min overloads

        public static int Min(int val1, int val2) => val1 <= val2 ? val1 : val2;

        public static long Min(long val1, long val2) => val1 <= val2 ? val1 : val2;

        public static float Min(float val1, float val2)
        {
            if (val1 < val2) return val1;
            if (float.IsNaN(val1)) return val1;
            return val2;
        }

        public static double Min(double val1, double val2)
        {
            if (val1 < val2) return val1;
            if (double.IsNaN(val1)) return val1;
            return val2;
        }

        public static byte Min(byte val1, byte val2) => val1 <= val2 ? val1 : val2;

        public static sbyte Min(sbyte val1, sbyte val2) => val1 <= val2 ? val1 : val2;

        public static short Min(short val1, short val2) => val1 <= val2 ? val1 : val2;

        public static ushort Min(ushort val1, ushort val2) => val1 <= val2 ? val1 : val2;

        public static uint Min(uint val1, uint val2) => val1 <= val2 ? val1 : val2;

        public static ulong Min(ulong val1, ulong val2) => val1 <= val2 ? val1 : val2;

        // Max overloads

        public static int Max(int val1, int val2) => val1 >= val2 ? val1 : val2;

        public static long Max(long val1, long val2) => val1 >= val2 ? val1 : val2;

        public static float Max(float val1, float val2)
        {
            if (val1 > val2) return val1;
            if (float.IsNaN(val1)) return val1;
            return val2;
        }

        public static double Max(double val1, double val2)
        {
            if (val1 > val2) return val1;
            if (double.IsNaN(val1)) return val1;
            return val2;
        }

        public static byte Max(byte val1, byte val2) => val1 >= val2 ? val1 : val2;

        public static sbyte Max(sbyte val1, sbyte val2) => val1 >= val2 ? val1 : val2;

        public static short Max(short val1, short val2) => val1 >= val2 ? val1 : val2;

        public static ushort Max(ushort val1, ushort val2) => val1 >= val2 ? val1 : val2;

        public static uint Max(uint val1, uint val2) => val1 >= val2 ? val1 : val2;

        public static ulong Max(ulong val1, ulong val2) => val1 >= val2 ? val1 : val2;

        // Abs overloads

        public static int Abs(int value)
        {
            if (value == int.MinValue)
                throw new OverflowException("Value is too small");
            return value >= 0 ? value : -value;
        }

        public static long Abs(long value)
        {
            if (value == long.MinValue)
                throw new OverflowException("Value is too small");
            return value >= 0 ? value : -value;
        }

        public static float Abs(float value) => value < 0 ? -value : value;

        public static double Abs(double value) => value < 0 ? -value : value;

        public static sbyte Abs(sbyte value)
        {
            if (value == sbyte.MinValue)
                throw new OverflowException("Value is too small");
            return value >= 0 ? value : (sbyte)-value;
        }

        public static short Abs(short value)
        {
            if (value == short.MinValue)
                throw new OverflowException("Value is too small");
            return value >= 0 ? value : (short)-value;
        }

        // Sign overloads

        public static int Sign(int value)
        {
            if (value < 0) return -1;
            if (value > 0) return 1;
            return 0;
        }

        public static int Sign(long value)
        {
            if (value < 0) return -1;
            if (value > 0) return 1;
            return 0;
        }

        public static int Sign(float value)
        {
            if (float.IsNaN(value)) throw new ArithmeticException("NaN is not a valid input");
            if (value < 0) return -1;
            if (value > 0) return 1;
            return 0;
        }

        public static int Sign(double value)
        {
            if (double.IsNaN(value)) throw new ArithmeticException("NaN is not a valid input");
            if (value < 0) return -1;
            if (value > 0) return 1;
            return 0;
        }

        public static int Sign(sbyte value) => Sign((int)value);

        public static int Sign(short value) => Sign((int)value);

        // Clamp overloads

        public static int Clamp(int value, int min, int max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static long Clamp(long value, long min, long max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static byte Clamp(byte value, byte min, byte max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static sbyte Clamp(sbyte value, sbyte min, sbyte max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static short Clamp(short value, short min, short max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static ushort Clamp(ushort value, ushort min, ushort max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static uint Clamp(uint value, uint min, uint max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static ulong Clamp(ulong value, ulong min, ulong max)
        {
            if (min > max) throw new ArgumentException("min must be less than or equal to max");
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // Rounding methods - software implementations

        public static double Floor(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d;
            long truncated = (long)d;
            if (d == truncated) return d;
            return d >= 0 ? truncated : truncated - 1;
        }

        public static double Ceiling(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d;
            long truncated = (long)d;
            if (d == truncated) return d;
            return d >= 0 ? truncated + 1 : truncated;
        }

        public static double Round(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d;
            double floor = Floor(d);
            double frac = d - floor;
            if (frac > 0.5) return floor + 1;
            if (frac < 0.5) return floor;
            // Banker's rounding: round to nearest even
            return ((long)floor & 1) == 0 ? floor : floor + 1;
        }

        public static double Truncate(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return d;
            return (double)(long)d;
        }

        // Sqrt - Newton-Raphson approximation
        public static double Sqrt(double d)
        {
            if (d < 0) return double.NaN;
            if (d == 0 || double.IsNaN(d) || double.IsPositiveInfinity(d)) return d;

            double guess = d / 2.0;
            for (int i = 0; i < 64; i++)
            {
                double newGuess = (guess + d / guess) / 2.0;
                if (newGuess == guess) break;
                guess = newGuess;
            }
            return guess;
        }

        // Pow - integer exponent fast path
        public static double Pow(double x, double y)
        {
            if (y == 0) return 1.0;
            if (x == 0) return y > 0 ? 0.0 : double.PositiveInfinity;
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;

            // Integer exponent fast path
            if (y == (long)y && y >= 0 && y <= 62)
            {
                long n = (long)y;
                double result = 1.0;
                double power = x;
                while (n > 0)
                {
                    if ((n & 1) == 1)
                        result *= power;
                    power *= power;
                    n >>= 1;
                }
                return result;
            }

            // General case: x^y = e^(y * ln(x))
            if (x < 0)
            {
                // Negative base with non-integer exponent
                return double.NaN;
            }
            return Exp(y * Log(x));
        }

        // Log - natural logarithm using Taylor series
        public static double Log(double d)
        {
            if (d < 0) return double.NaN;
            if (d == 0) return double.NegativeInfinity;
            if (double.IsNaN(d) || double.IsPositiveInfinity(d)) return d;
            if (d == 1) return 0;

            // Reduce to range [0.5, 1.5] for better convergence
            int exponent = 0;
            while (d >= 2)
            {
                d /= E;
                exponent++;
            }
            while (d < 0.5)
            {
                d *= E;
                exponent--;
            }

            // Taylor series: ln(1+x) = x - x^2/2 + x^3/3 - ...
            double x = (d - 1) / (d + 1);
            double x2 = x * x;
            double result = 0;
            double term = x;
            for (int n = 1; n <= 100; n += 2)
            {
                result += term / n;
                term *= x2;
                if (Abs(term) < 1e-15) break;
            }
            return 2 * result + exponent;
        }

        public static double Log10(double d)
        {
            const double LOG10_E = 0.43429448190325182; // 1/ln(10)
            return Log(d) * LOG10_E;
        }

        public static double Log2(double d)
        {
            const double LOG2_E = 1.4426950408889634; // 1/ln(2)
            return Log(d) * LOG2_E;
        }

        // Exp - e^x using Taylor series
        public static double Exp(double d)
        {
            if (double.IsNaN(d)) return double.NaN;
            if (double.IsPositiveInfinity(d)) return double.PositiveInfinity;
            if (double.IsNegativeInfinity(d)) return 0;
            if (d == 0) return 1;

            // Reduce large arguments
            bool negate = d < 0;
            if (negate) d = -d;

            // Reduce to range [0, ln(2)] using e^x = e^(x - k*ln(2)) * 2^k
            int k = 0;
            const double LN2 = 0.6931471805599453;
            while (d > LN2)
            {
                d -= LN2;
                k++;
            }

            // Taylor series: e^x = 1 + x + x^2/2! + x^3/3! + ...
            double result = 1;
            double term = 1;
            for (int n = 1; n <= 30; n++)
            {
                term *= d / n;
                result += term;
                if (Abs(term) < 1e-15) break;
            }

            // Apply 2^k multiplier
            while (k > 0)
            {
                result *= 2;
                k--;
            }

            return negate ? 1 / result : result;
        }

        // Trigonometric functions using Taylor series

        public static double Sin(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return double.NaN;

            // Reduce to [-PI, PI]
            d = d % (2 * PI);
            if (d > PI) d -= 2 * PI;
            if (d < -PI) d += 2 * PI;

            // Taylor series: sin(x) = x - x^3/3! + x^5/5! - ...
            double x2 = d * d;
            double result = d;
            double term = d;
            for (int n = 1; n <= 15; n++)
            {
                term *= -x2 / ((2 * n) * (2 * n + 1));
                result += term;
                if (Abs(term) < 1e-15) break;
            }
            return result;
        }

        public static double Cos(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return double.NaN;

            // Reduce to [-PI, PI]
            d = d % (2 * PI);
            if (d > PI) d -= 2 * PI;
            if (d < -PI) d += 2 * PI;

            // Taylor series: cos(x) = 1 - x^2/2! + x^4/4! - ...
            double x2 = d * d;
            double result = 1;
            double term = 1;
            for (int n = 1; n <= 15; n++)
            {
                term *= -x2 / ((2 * n - 1) * (2 * n));
                result += term;
                if (Abs(term) < 1e-15) break;
            }
            return result;
        }

        public static double Tan(double d)
        {
            double c = Cos(d);
            if (c == 0) return double.NaN;
            return Sin(d) / c;
        }

        // Inverse trigonometric functions

        public static double Asin(double d)
        {
            if (d < -1 || d > 1) return double.NaN;
            if (d == 0) return 0;
            if (d == 1) return PI / 2;
            if (d == -1) return -PI / 2;

            // Use identity: asin(x) = atan(x / sqrt(1 - x^2))
            return Atan(d / Sqrt(1 - d * d));
        }

        public static double Acos(double d)
        {
            if (d < -1 || d > 1) return double.NaN;
            return PI / 2 - Asin(d);
        }

        public static double Atan(double d)
        {
            if (double.IsNaN(d)) return double.NaN;
            if (double.IsPositiveInfinity(d)) return PI / 2;
            if (double.IsNegativeInfinity(d)) return -PI / 2;

            bool negate = d < 0;
            if (negate) d = -d;

            bool invert = d > 1;
            if (invert) d = 1 / d;

            // Taylor series: atan(x) = x - x^3/3 + x^5/5 - ...
            double x2 = d * d;
            double result = d;
            double term = d;
            for (int n = 1; n <= 50; n++)
            {
                term *= -x2;
                result += term / (2 * n + 1);
                if (Abs(term) < 1e-15) break;
            }

            if (invert) result = PI / 2 - result;
            return negate ? -result : result;
        }

        public static double Atan2(double y, double x)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;

            if (x > 0) return Atan(y / x);
            if (x < 0 && y >= 0) return Atan(y / x) + PI;
            if (x < 0 && y < 0) return Atan(y / x) - PI;
            if (x == 0 && y > 0) return PI / 2;
            if (x == 0 && y < 0) return -PI / 2;
            return 0; // x == 0 && y == 0
        }

        // DivRem helpers

        public static int DivRem(int a, int b, out int result)
        {
            int div = a / b;
            result = a - (div * b);
            return div;
        }

        public static long DivRem(long a, long b, out long result)
        {
            long div = a / b;
            result = a - (div * b);
            return div;
        }

        // BigMul for 64-bit result from 32-bit operands
        public static long BigMul(int a, int b)
        {
            return (long)a * b;
        }
    }
}
