// ProtonOS System.Runtime - DateTime
// Represents an instant in time.

namespace System
{
    /// <summary>
    /// Represents an instant in time, typically expressed as a date and time of day.
    /// </summary>
    public readonly struct DateTime : IEquatable<DateTime>, IComparable<DateTime>, IComparable
    {
        // Ticks per time unit
        private const long TicksPerMillisecond = 10000;
        private const long TicksPerSecond = TicksPerMillisecond * 1000;
        private const long TicksPerMinute = TicksPerSecond * 60;
        private const long TicksPerHour = TicksPerMinute * 60;
        private const long TicksPerDay = TicksPerHour * 24;

        // Number of days from 1/1/0001 to 12/31/1600
        private const int DaysTo1601 = 584388;
        // Number of days from 1/1/0001 to 12/30/1899
        private const int DaysTo1899 = 693593;
        // Number of days from 1/1/0001 to 12/31/1969
        private const int DaysTo1970 = 719162;
        // Number of days from 1/1/0001 to 12/31/9999
        private const int DaysTo10000 = 3652059;

        internal const long MinTicks = 0;
        internal const long MaxTicks = DaysTo10000 * TicksPerDay - 1;

        // Days in each month (non-leap year)
        private static readonly int[] DaysToMonth365 = { 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365 };
        // Days in each month (leap year)
        private static readonly int[] DaysToMonth366 = { 0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366 };

        public static readonly DateTime MinValue = new DateTime(MinTicks, DateTimeKind.Unspecified);
        public static readonly DateTime MaxValue = new DateTime(MaxTicks, DateTimeKind.Unspecified);
        public static readonly DateTime UnixEpoch = new DateTime(DaysTo1970 * TicksPerDay, DateTimeKind.Utc);

        // Bits 01-62: ticks, bits 63-64: kind
        private readonly ulong _dateData;

        /// <summary>Gets the number of ticks that represent the date and time of this instance.</summary>
        public long Ticks => (long)(_dateData & 0x3FFFFFFFFFFFFFFFUL);

        /// <summary>Gets a value that indicates whether the time represented by this instance is local time, UTC, or neither.</summary>
        public DateTimeKind Kind => (DateTimeKind)(_dateData >> 62);

        /// <summary>Gets the date component of this instance.</summary>
        public DateTime Date => new DateTime((long)(_dateData & 0x3FFFFFFFFFFFFFFFUL) - (Ticks % TicksPerDay), Kind);

        /// <summary>Gets the time of day for this instance.</summary>
        public TimeSpan TimeOfDay => new TimeSpan(Ticks % TicksPerDay);

        /// <summary>Gets the year component of the date represented by this instance.</summary>
        public int Year => GetDatePart(0);

        /// <summary>Gets the month component of the date represented by this instance.</summary>
        public int Month => GetDatePart(2);

        /// <summary>Gets the day of the month represented by this instance.</summary>
        public int Day => GetDatePart(3);

        /// <summary>Gets the hour component of the date represented by this instance.</summary>
        public int Hour => (int)((Ticks / TicksPerHour) % 24);

        /// <summary>Gets the minute component of the date represented by this instance.</summary>
        public int Minute => (int)((Ticks / TicksPerMinute) % 60);

        /// <summary>Gets the second component of the date represented by this instance.</summary>
        public int Second => (int)((Ticks / TicksPerSecond) % 60);

        /// <summary>Gets the millisecond component of the date represented by this instance.</summary>
        public int Millisecond => (int)((Ticks / TicksPerMillisecond) % 1000);

        /// <summary>Gets the day of the week represented by this instance.</summary>
        public DayOfWeek DayOfWeek => (DayOfWeek)((Ticks / TicksPerDay + 1) % 7);

        /// <summary>Gets the day of the year represented by this instance.</summary>
        public int DayOfYear => GetDatePart(1);

        /// <summary>Initializes a new DateTime to a specified number of ticks.</summary>
        public DateTime(long ticks)
        {
            if (ticks < MinTicks || ticks > MaxTicks)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            _dateData = (ulong)ticks;
        }

        /// <summary>Initializes a new DateTime to a specified number of ticks and DateTimeKind.</summary>
        public DateTime(long ticks, DateTimeKind kind)
        {
            if (ticks < MinTicks || ticks > MaxTicks)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            _dateData = (ulong)ticks | ((ulong)kind << 62);
        }

        /// <summary>Initializes a new DateTime to the specified year, month, and day.</summary>
        public DateTime(int year, int month, int day)
            : this(year, month, day, 0, 0, 0, 0, DateTimeKind.Unspecified)
        {
        }

        /// <summary>Initializes a new DateTime to the specified year, month, day, hour, minute, and second.</summary>
        public DateTime(int year, int month, int day, int hour, int minute, int second)
            : this(year, month, day, hour, minute, second, 0, DateTimeKind.Unspecified)
        {
        }

        /// <summary>Initializes a new DateTime to the specified year, month, day, hour, minute, second, and millisecond.</summary>
        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
            : this(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified)
        {
        }

        /// <summary>Initializes a new DateTime to the specified year, month, day, hour, minute, second, millisecond, and DateTimeKind.</summary>
        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond, DateTimeKind kind)
        {
            long ticks = DateToTicks(year, month, day) + TimeToTicks(hour, minute, second) + millisecond * TicksPerMillisecond;
            _dateData = (ulong)ticks | ((ulong)kind << 62);
        }

        private static long DateToTicks(int year, int month, int day)
        {
            if (year < 1 || year > 9999 || month < 1 || month > 12)
                throw new ArgumentOutOfRangeException();

            int[] days = IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
            if (day < 1 || day > days[month] - days[month - 1])
                throw new ArgumentOutOfRangeException();

            int y = year - 1;
            int n = y * 365 + y / 4 - y / 100 + y / 400 + days[month - 1] + day - 1;
            return n * TicksPerDay;
        }

        private static long TimeToTicks(int hour, int minute, int second)
        {
            if (hour < 0 || hour >= 24 || minute < 0 || minute >= 60 || second < 0 || second >= 60)
                throw new ArgumentOutOfRangeException();
            return hour * TicksPerHour + minute * TicksPerMinute + second * TicksPerSecond;
        }

        public static bool IsLeapYear(int year)
        {
            if (year < 1 || year > 9999)
                throw new ArgumentOutOfRangeException(nameof(year));
            return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
        }

        private int GetDatePart(int part)
        {
            int n = (int)(Ticks / TicksPerDay);
            int y400 = n / 146097; // days in 400 years
            n -= y400 * 146097;
            int y100 = n / 36524; // days in 100 years
            if (y100 == 4) y100 = 3;
            n -= y100 * 36524;
            int y4 = n / 1461; // days in 4 years
            n -= y4 * 1461;
            int y1 = n / 365;
            if (y1 == 4) y1 = 3;
            if (part == 0) return y400 * 400 + y100 * 100 + y4 * 4 + y1 + 1;
            n -= y1 * 365;
            if (part == 1) return n + 1;
            int[] days = y1 == 3 && (y4 != 24 || y100 == 3) ? DaysToMonth366 : DaysToMonth365;
            int m = (n >> 5) + 1;
            while (n >= days[m]) m++;
            if (part == 2) return m;
            return n - days[m - 1] + 1;
        }

        // Arithmetic
        public DateTime Add(TimeSpan value) => new DateTime(Ticks + value.Ticks, Kind);
        public DateTime AddDays(double value) => Add(TimeSpan.FromDays(value));
        public DateTime AddHours(double value) => Add(TimeSpan.FromHours(value));
        public DateTime AddMinutes(double value) => Add(TimeSpan.FromMinutes(value));
        public DateTime AddSeconds(double value) => Add(TimeSpan.FromSeconds(value));
        public DateTime AddMilliseconds(double value) => Add(TimeSpan.FromMilliseconds(value));
        public DateTime AddTicks(long value) => new DateTime(Ticks + value, Kind);

        public DateTime AddMonths(int months)
        {
            int y = Year;
            int m = Month;
            int d = Day;
            int i = m - 1 + months;
            if (i >= 0)
            {
                m = i % 12 + 1;
                y += i / 12;
            }
            else
            {
                m = 12 + (i + 1) % 12;
                y += (i - 11) / 12;
            }
            int[] days = IsLeapYear(y) ? DaysToMonth366 : DaysToMonth365;
            int daysInMonth = days[m] - days[m - 1];
            if (d > daysInMonth) d = daysInMonth;
            return new DateTime(DateToTicks(y, m, d) + Ticks % TicksPerDay, Kind);
        }

        public DateTime AddYears(int value) => AddMonths(value * 12);

        public TimeSpan Subtract(DateTime value) => new TimeSpan(Ticks - value.Ticks);
        public DateTime Subtract(TimeSpan value) => new DateTime(Ticks - value.Ticks, Kind);

        // Operators
        public static DateTime operator +(DateTime d, TimeSpan t) => d.Add(t);
        public static DateTime operator -(DateTime d, TimeSpan t) => d.Subtract(t);
        public static TimeSpan operator -(DateTime d1, DateTime d2) => d1.Subtract(d2);

        public static bool operator ==(DateTime d1, DateTime d2) => d1.Ticks == d2.Ticks;
        public static bool operator !=(DateTime d1, DateTime d2) => d1.Ticks != d2.Ticks;
        public static bool operator <(DateTime d1, DateTime d2) => d1.Ticks < d2.Ticks;
        public static bool operator <=(DateTime d1, DateTime d2) => d1.Ticks <= d2.Ticks;
        public static bool operator >(DateTime d1, DateTime d2) => d1.Ticks > d2.Ticks;
        public static bool operator >=(DateTime d1, DateTime d2) => d1.Ticks >= d2.Ticks;

        // Comparison and equality
        public int CompareTo(DateTime other) => Ticks.CompareTo(other.Ticks);

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not DateTime dt) throw new ArgumentException("Object must be DateTime");
            return CompareTo(dt);
        }

        public bool Equals(DateTime other) => Ticks == other.Ticks;
        public override bool Equals(object? obj) => obj is DateTime dt && Ticks == dt.Ticks;
        public override int GetHashCode() => Ticks.GetHashCode();

        public override string ToString()
        {
            // ISO 8601 format: yyyy-MM-dd HH:mm:ss
            return Year.ToString().PadLeft(4, '0') + "-" +
                   Month.ToString().PadLeft(2, '0') + "-" +
                   Day.ToString().PadLeft(2, '0') + " " +
                   Hour.ToString().PadLeft(2, '0') + ":" +
                   Minute.ToString().PadLeft(2, '0') + ":" +
                   Second.ToString().PadLeft(2, '0');
        }

        /// <summary>Converts the value of the current DateTime object to UTC.</summary>
        public DateTime ToUniversalTime()
        {
            // In kernel mode, we assume UTC for now
            return new DateTime(Ticks, DateTimeKind.Utc);
        }

        /// <summary>Gets a DateTime object that is set to the current date and time on this computer, expressed as UTC.</summary>
        public static DateTime UtcNow
        {
            get
            {
                // This would need to be hooked to the kernel's RTC
                // For now, return Unix epoch as a placeholder
                return UnixEpoch;
            }
        }

        /// <summary>Gets the current date.</summary>
        public static DateTime Today => UtcNow.Date;

        /// <summary>Gets a DateTime object that is set to the current date and time on this computer, expressed as local time.</summary>
        public static DateTime Now => UtcNow; // In kernel mode, local == UTC
    }

    /// <summary>
    /// Specifies the day of the week.
    /// </summary>
    public enum DayOfWeek
    {
        Sunday = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6
    }
}
