// ProtonOS korlib - DateTime
// Represents an instant in time.

namespace System
{
    /// <summary>
    /// Represents an instant in time, typically expressed as a date and time of day.
    /// </summary>
    /// <remarks>
    /// Note: Generic interfaces (IEquatable&lt;DateTime&gt;, IComparable&lt;DateTime&gt;) are not
    /// implemented to avoid JIT issues with generic interface dispatch on value types.
    /// Use the Equals(DateTime) and CompareTo(DateTime) methods directly.
    /// </remarks>
    public readonly struct DateTime : IComparable
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

        public static readonly DateTime MinValue = new DateTime(MinTicks, DateTimeKind.Unspecified);
        public static readonly DateTime MaxValue = new DateTime(MaxTicks, DateTimeKind.Unspecified);
        public static readonly DateTime UnixEpoch = new DateTime(DaysTo1970 * TicksPerDay, DateTimeKind.Utc);

        // Helper to get cumulative days to start of month (avoids static arrays)
        private static int GetDaysToMonth(int month, bool isLeapYear)
        {
            // Cumulative days: Jan=0, Feb=31, Mar=59/60, etc.
            if (month == 0) return 0;
            if (month == 1) return 31;
            if (month == 2) return isLeapYear ? 60 : 59;
            if (month == 3) return isLeapYear ? 91 : 90;
            if (month == 4) return isLeapYear ? 121 : 120;
            if (month == 5) return isLeapYear ? 152 : 151;
            if (month == 6) return isLeapYear ? 182 : 181;
            if (month == 7) return isLeapYear ? 213 : 212;
            if (month == 8) return isLeapYear ? 244 : 243;
            if (month == 9) return isLeapYear ? 274 : 273;
            if (month == 10) return isLeapYear ? 305 : 304;
            if (month == 11) return isLeapYear ? 335 : 334;
            if (month == 12) return isLeapYear ? 366 : 365;
            return 0;
        }

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
            // Bounds check removed for JIT compatibility
            _dateData = (ulong)ticks;
        }

        /// <summary>Initializes a new DateTime to a specified number of ticks and DateTimeKind.</summary>
        public DateTime(long ticks, DateTimeKind kind)
        {
            // Bounds check removed for JIT compatibility
            _dateData = (ulong)ticks | ((ulong)kind << 62);
        }

        /// <summary>Initializes a new DateTime to the specified year, month, and day.</summary>
        public DateTime(int year, int month, int day)
        {
            long ticks = DateToTicks(year, month, day);
            _dateData = (ulong)ticks;
        }

        /// <summary>Initializes a new DateTime to the specified year, month, day, hour, minute, and second.</summary>
        public DateTime(int year, int month, int day, int hour, int minute, int second)
        {
            long ticks = DateToTicks(year, month, day) + TimeToTicks(hour, minute, second);
            _dateData = (ulong)ticks;
        }

        /// <summary>Initializes a new DateTime to the specified year, month, day, hour, minute, second, and millisecond.</summary>
        public DateTime(int year, int month, int day, int hour, int minute, int second, int millisecond)
        {
            long ticks = DateToTicks(year, month, day) + TimeToTicks(hour, minute, second) + millisecond * TicksPerMillisecond;
            _dateData = (ulong)ticks;
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

            bool isLeap = IsLeapYear(year);
            int daysInMonth = GetDaysToMonth(month, isLeap) - GetDaysToMonth(month - 1, isLeap);
            if (day < 1 || day > daysInMonth)
                throw new ArgumentOutOfRangeException();

            int y = year - 1;
            int n = y * 365 + y / 4 - y / 100 + y / 400 + GetDaysToMonth(month - 1, isLeap) + day - 1;
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
            bool isLeap = y1 == 3 && (y4 != 24 || y100 == 3);
            int m = (n >> 5) + 1;
            while (n >= GetDaysToMonth(m, isLeap)) m++;
            if (part == 2) return m;
            return n - GetDaysToMonth(m - 1, isLeap) + 1;
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
            bool isLeap = IsLeapYear(y);
            int daysInMonth = GetDaysToMonth(m, isLeap) - GetDaysToMonth(m - 1, isLeap);
            if (d > daysInMonth) d = daysInMonth;
            return new DateTime(DateToTicks(y, m, d) + Ticks % TicksPerDay, Kind);
        }

        public DateTime AddYears(int value) => AddMonths(value * 12);

        public TimeSpan Subtract(DateTime value) => new TimeSpan(Ticks - value.Ticks);
        public DateTime Subtract(TimeSpan value) => new DateTime(Ticks - value.Ticks, Kind);

        /// <summary>Returns the number of days in the specified month and year.</summary>
        public static int DaysInMonth(int year, int month)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month));
            bool isLeap = IsLeapYear(year);
            return GetDaysToMonth(month, isLeap) - GetDaysToMonth(month - 1, isLeap);
        }

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
        public int CompareTo(DateTime other)
        {
            long t1 = Ticks;
            long t2 = other.Ticks;
            if (t1 < t2) return -1;
            if (t1 > t2) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not DateTime dt) throw new ArgumentException("Object must be DateTime");
            return CompareTo(dt);
        }

        public bool Equals(DateTime other) => Ticks == other.Ticks;
        public override bool Equals(object? obj) => obj is DateTime dt && Ticks == dt.Ticks;
        public override int GetHashCode()
        {
            long ticks = Ticks;
            return (int)ticks ^ (int)(ticks >> 32);
        }

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

        /// <summary>Converts the value of the current DateTime object to local time.</summary>
        public DateTime ToLocalTime()
        {
            // In kernel mode, local == UTC
            return new DateTime(Ticks, DateTimeKind.Local);
        }

        /// <summary>Returns a new DateTime with the same date/time but with the specified DateTimeKind.</summary>
        public static DateTime SpecifyKind(DateTime value, DateTimeKind kind)
        {
            return new DateTime(value.Ticks, kind);
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
