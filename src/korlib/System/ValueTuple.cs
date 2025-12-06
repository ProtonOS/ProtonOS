// ProtonOS korlib - ValueTuple types for C# 7.0+ tuple syntax
// Based on .NET runtime ValueTuple implementation

using System.Collections;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// Defines a general-purpose tuple type that can be accessed by position.
    /// </summary>
    public interface ITuple
    {
        /// <summary>Gets the element at the specified index.</summary>
        object? this[int index] { get; }

        /// <summary>Gets the number of elements in this tuple.</summary>
        int Length { get; }
    }

    /// <summary>
    /// Provides static methods for creating value tuples.
    /// </summary>
    public static class ValueTuple
    {
        /// <summary>Creates a new 0-tuple.</summary>
        public static ValueTuple<T1> Create<T1>(T1 item1) =>
            new ValueTuple<T1>(item1);

        /// <summary>Creates a new 2-tuple.</summary>
        public static ValueTuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) =>
            new ValueTuple<T1, T2>(item1, item2);

        /// <summary>Creates a new 3-tuple.</summary>
        public static ValueTuple<T1, T2, T3> Create<T1, T2, T3>(T1 item1, T2 item2, T3 item3) =>
            new ValueTuple<T1, T2, T3>(item1, item2, item3);

        /// <summary>Creates a new 4-tuple.</summary>
        public static ValueTuple<T1, T2, T3, T4> Create<T1, T2, T3, T4>(T1 item1, T2 item2, T3 item3, T4 item4) =>
            new ValueTuple<T1, T2, T3, T4>(item1, item2, item3, item4);

        /// <summary>Creates a new 5-tuple.</summary>
        public static ValueTuple<T1, T2, T3, T4, T5> Create<T1, T2, T3, T4, T5>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5) =>
            new ValueTuple<T1, T2, T3, T4, T5>(item1, item2, item3, item4, item5);

        /// <summary>Creates a new 6-tuple.</summary>
        public static ValueTuple<T1, T2, T3, T4, T5, T6> Create<T1, T2, T3, T4, T5, T6>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6) =>
            new ValueTuple<T1, T2, T3, T4, T5, T6>(item1, item2, item3, item4, item5, item6);

        /// <summary>Creates a new 7-tuple.</summary>
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7> Create<T1, T2, T3, T4, T5, T6, T7>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7) =>
            new ValueTuple<T1, T2, T3, T4, T5, T6, T7>(item1, item2, item3, item4, item5, item6, item7);

        /// <summary>Creates a new 8-tuple.</summary>
        public static ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8>> Create<T1, T2, T3, T4, T5, T6, T7, T8>(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, T8 item8) =>
            new ValueTuple<T1, T2, T3, T4, T5, T6, T7, ValueTuple<T8>>(item1, item2, item3, item4, item5, item6, item7, new ValueTuple<T8>(item8));
    }

    /// <summary>Represents a 1-tuple, or singleton.</summary>
    public struct ValueTuple<T1> : IEquatable<ValueTuple<T1>>, ITuple
    {
        public T1 Item1;

        public ValueTuple(T1 item1)
        {
            Item1 = item1;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1> other && Equals(other);

        public bool Equals(ValueTuple<T1> other) =>
            EqualityComparer.Equals(Item1, other.Item1);

        public override int GetHashCode() =>
            Item1?.GetHashCode() ?? 0;

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 1;
        object? ITuple.this[int index] => index == 0 ? Item1 : throw new IndexOutOfRangeException();
    }

    /// <summary>Represents a 2-tuple, or pair.</summary>
    public struct ValueTuple<T1, T2> : IEquatable<ValueTuple<T1, T2>>, ITuple
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2);

        public override int GetHashCode()
        {
            int h1 = Item1?.GetHashCode() ?? 0;
            int h2 = Item2?.GetHashCode() ?? 0;
            return ((h1 << 5) + h1) ^ h2;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 2;
        object? ITuple.this[int index] => index switch
        {
            0 => Item1,
            1 => Item2,
            _ => throw new IndexOutOfRangeException()
        };
    }

    /// <summary>Represents a 3-tuple.</summary>
    public struct ValueTuple<T1, T2, T3> : IEquatable<ValueTuple<T1, T2, T3>>, ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2, T3> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2, T3> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2) &&
            EqualityComparer.Equals(Item3, other.Item3);

        public override int GetHashCode()
        {
            int hash = Item1?.GetHashCode() ?? 0;
            hash = ((hash << 5) + hash) ^ (Item2?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item3?.GetHashCode() ?? 0);
            return hash;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 3;
        object? ITuple.this[int index] => index switch
        {
            0 => Item1,
            1 => Item2,
            2 => Item3,
            _ => throw new IndexOutOfRangeException()
        };
    }

    /// <summary>Represents a 4-tuple.</summary>
    public struct ValueTuple<T1, T2, T3, T4> : IEquatable<ValueTuple<T1, T2, T3, T4>>, ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2, T3, T4> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2, T3, T4> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2) &&
            EqualityComparer.Equals(Item3, other.Item3) &&
            EqualityComparer.Equals(Item4, other.Item4);

        public override int GetHashCode()
        {
            int hash = Item1?.GetHashCode() ?? 0;
            hash = ((hash << 5) + hash) ^ (Item2?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item3?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item4?.GetHashCode() ?? 0);
            return hash;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 4;
        object? ITuple.this[int index] => index switch
        {
            0 => Item1,
            1 => Item2,
            2 => Item3,
            3 => Item4,
            _ => throw new IndexOutOfRangeException()
        };
    }

    /// <summary>Represents a 5-tuple.</summary>
    public struct ValueTuple<T1, T2, T3, T4, T5> : IEquatable<ValueTuple<T1, T2, T3, T4, T5>>, ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2, T3, T4, T5> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2) &&
            EqualityComparer.Equals(Item3, other.Item3) &&
            EqualityComparer.Equals(Item4, other.Item4) &&
            EqualityComparer.Equals(Item5, other.Item5);

        public override int GetHashCode()
        {
            int hash = Item1?.GetHashCode() ?? 0;
            hash = ((hash << 5) + hash) ^ (Item2?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item3?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item4?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item5?.GetHashCode() ?? 0);
            return hash;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 5;
        object? ITuple.this[int index] => index switch
        {
            0 => Item1,
            1 => Item2,
            2 => Item3,
            3 => Item4,
            4 => Item5,
            _ => throw new IndexOutOfRangeException()
        };
    }

    /// <summary>Represents a 6-tuple.</summary>
    public struct ValueTuple<T1, T2, T3, T4, T5, T6> : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6>>, ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2, T3, T4, T5, T6> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2) &&
            EqualityComparer.Equals(Item3, other.Item3) &&
            EqualityComparer.Equals(Item4, other.Item4) &&
            EqualityComparer.Equals(Item5, other.Item5) &&
            EqualityComparer.Equals(Item6, other.Item6);

        public override int GetHashCode()
        {
            int hash = Item1?.GetHashCode() ?? 0;
            hash = ((hash << 5) + hash) ^ (Item2?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item3?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item4?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item5?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item6?.GetHashCode() ?? 0);
            return hash;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 6;
        object? ITuple.this[int index] => index switch
        {
            0 => Item1,
            1 => Item2,
            2 => Item3,
            3 => Item4,
            4 => Item5,
            5 => Item6,
            _ => throw new IndexOutOfRangeException()
        };
    }

    /// <summary>Represents a 7-tuple.</summary>
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7> : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7>>, ITuple
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2, T3, T4, T5, T6, T7> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2) &&
            EqualityComparer.Equals(Item3, other.Item3) &&
            EqualityComparer.Equals(Item4, other.Item4) &&
            EqualityComparer.Equals(Item5, other.Item5) &&
            EqualityComparer.Equals(Item6, other.Item6) &&
            EqualityComparer.Equals(Item7, other.Item7);

        public override int GetHashCode()
        {
            int hash = Item1?.GetHashCode() ?? 0;
            hash = ((hash << 5) + hash) ^ (Item2?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item3?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item4?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item5?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item6?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item7?.GetHashCode() ?? 0);
            return hash;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 7;
        object? ITuple.this[int index] => index switch
        {
            0 => Item1,
            1 => Item2,
            2 => Item3,
            3 => Item4,
            4 => Item5,
            5 => Item6,
            6 => Item7,
            _ => throw new IndexOutOfRangeException()
        };
    }

    /// <summary>Represents an 8+ tuple with a rest element for overflow.</summary>
    public struct ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> : IEquatable<ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest>>, ITuple
        where TRest : struct
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;
        public T5 Item5;
        public T6 Item6;
        public T7 Item7;
        public TRest Rest;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4, T5 item5, T6 item6, T7 item7, TRest rest)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
            Item5 = item5;
            Item6 = item6;
            Item7 = item7;
            Rest = rest;
        }

        public override bool Equals(object? obj) =>
            obj is ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other && Equals(other);

        public bool Equals(ValueTuple<T1, T2, T3, T4, T5, T6, T7, TRest> other) =>
            EqualityComparer.Equals(Item1, other.Item1) &&
            EqualityComparer.Equals(Item2, other.Item2) &&
            EqualityComparer.Equals(Item3, other.Item3) &&
            EqualityComparer.Equals(Item4, other.Item4) &&
            EqualityComparer.Equals(Item5, other.Item5) &&
            EqualityComparer.Equals(Item6, other.Item6) &&
            EqualityComparer.Equals(Item7, other.Item7) &&
            EqualityComparer.Equals(Rest, other.Rest);

        public override int GetHashCode()
        {
            int hash = Item1?.GetHashCode() ?? 0;
            hash = ((hash << 5) + hash) ^ (Item2?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item3?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item4?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item5?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item6?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ (Item7?.GetHashCode() ?? 0);
            hash = ((hash << 5) + hash) ^ Rest.GetHashCode();
            return hash;
        }

        public override string ToString() => "ValueTuple";

        int ITuple.Length => 7 + (Rest is ITuple t ? t.Length : 1);

        object? ITuple.this[int index]
        {
            get
            {
                return index switch
                {
                    0 => Item1,
                    1 => Item2,
                    2 => Item3,
                    3 => Item4,
                    4 => Item5,
                    5 => Item6,
                    6 => Item7,
                    _ => Rest is ITuple t ? t[index - 7] : (index == 7 ? Rest : throw new IndexOutOfRangeException())
                };
            }
        }
    }

    /// <summary>
    /// Simple equality comparer helper for ValueTuple equality checks.
    /// </summary>
    internal static class EqualityComparer
    {
        public static bool Equals<T>(T? x, T? y)
        {
            if (x == null) return y == null;
            if (y == null) return false;
            return x.Equals(y);
        }
    }
}
