// ProtonOS korlib - Version
// Represents the version number of an assembly, operating system, or the common language runtime.

namespace System
{
    /// <summary>
    /// Represents the version number of an assembly, operating system, or the common language runtime.
    /// </summary>
    public sealed class Version : ICloneable, IComparable, IComparable<Version?>, IEquatable<Version?>
    {
        private readonly int _major;
        private readonly int _minor;
        private readonly int _build;
        private readonly int _revision;

        /// <summary>Initializes a new instance of the Version class with the specified major, minor, build, and revision numbers.</summary>
        public Version(int major, int minor, int build, int revision)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            // Note: build and revision can be -1 to indicate "undefined" (per .NET standard)
            if (build < -1) throw new ArgumentOutOfRangeException(nameof(build));
            if (revision < -1) throw new ArgumentOutOfRangeException(nameof(revision));
            _major = major;
            _minor = minor;
            _build = build;
            _revision = revision;
        }

        /// <summary>Initializes a new instance of the Version class using the specified major, minor, and build values.</summary>
        public Version(int major, int minor, int build) : this(major, minor, build, -1) { }

        /// <summary>Initializes a new instance of the Version class using the specified major and minor values.</summary>
        public Version(int major, int minor) : this(major, minor, -1, -1) { }

        /// <summary>Initializes a new instance of the Version class.</summary>
        public Version() : this(0, 0, -1, -1) { }

        /// <summary>Initializes a new instance of the Version class using the specified string.</summary>
        public Version(string version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            var parts = version.Split('.');
            if (parts.Length < 2) throw new ArgumentException("Invalid version string");
            _major = int.Parse(parts[0]);
            _minor = int.Parse(parts[1]);
            _build = parts.Length > 2 ? int.Parse(parts[2]) : -1;
            _revision = parts.Length > 3 ? int.Parse(parts[3]) : -1;
        }

        /// <summary>Gets the value of the major component of the version number.</summary>
        public int Major => _major;

        /// <summary>Gets the value of the minor component of the version number.</summary>
        public int Minor => _minor;

        /// <summary>Gets the value of the build component of the version number.</summary>
        public int Build => _build;

        /// <summary>Gets the value of the revision component of the version number.</summary>
        public int Revision => _revision;

        /// <summary>Gets the high 16 bits of the revision number.</summary>
        public short MajorRevision => (short)(_revision >> 16);

        /// <summary>Gets the low 16 bits of the revision number.</summary>
        public short MinorRevision => (short)(_revision & 0xFFFF);

        /// <summary>Returns a new Version object whose value is the same as the current Version object.</summary>
        public object Clone() => new Version(_major, _minor, _build, _revision);

        /// <summary>Compares the current Version object to a specified object and returns an indication of their relative values.</summary>
        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is Version v) return CompareTo(v);
            throw new ArgumentException("Object must be of type Version");
        }

        /// <summary>Compares the current Version object to a specified Version object and returns an indication of their relative values.</summary>
        public int CompareTo(Version? value)
        {
            if (value is null) return 1;
            if (_major != value._major) return _major > value._major ? 1 : -1;
            if (_minor != value._minor) return _minor > value._minor ? 1 : -1;
            if (_build != value._build) return _build > value._build ? 1 : -1;
            if (_revision != value._revision) return _revision > value._revision ? 1 : -1;
            return 0;
        }

        /// <summary>Returns a value indicating whether the current Version object is equal to a specified object.</summary>
        public override bool Equals(object? obj) => obj is Version v && Equals(v);

        /// <summary>Returns a value indicating whether the current Version object and a specified Version object represent the same value.</summary>
        public bool Equals(Version? other)
        {
            if (other is null) return false;
            return _major == other._major && _minor == other._minor &&
                   _build == other._build && _revision == other._revision;
        }

        /// <summary>Returns a hash code for the current Version object.</summary>
        public override int GetHashCode()
        {
            int hash = _major;
            hash = (hash * 397) ^ _minor;
            hash = (hash * 397) ^ _build;
            hash = (hash * 397) ^ _revision;
            return hash;
        }

        /// <summary>Converts the value of the current Version object to its equivalent string representation.</summary>
        public override string ToString()
        {
            if (_build < 0) return $"{_major}.{_minor}";
            if (_revision < 0) return $"{_major}.{_minor}.{_build}";
            return $"{_major}.{_minor}.{_build}.{_revision}";
        }

        /// <summary>Converts the value of the current Version object to its equivalent string representation with the specified field count.</summary>
        public string ToString(int fieldCount)
        {
            return fieldCount switch
            {
                0 => string.Empty,
                1 => _major.ToString(),
                2 => $"{_major}.{_minor}",
                3 => $"{_major}.{_minor}.{(_build < 0 ? 0 : _build)}",
                4 => $"{_major}.{_minor}.{(_build < 0 ? 0 : _build)}.{(_revision < 0 ? 0 : _revision)}",
                _ => throw new ArgumentException("Invalid field count")
            };
        }

        /// <summary>Tries to convert the string representation of a version number to an equivalent Version object.</summary>
        public static bool TryParse(string? input, out Version? result)
        {
            result = null;
            if (string.IsNullOrEmpty(input)) return false;
            try
            {
                result = new Version(input);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Converts the string representation of a version number to an equivalent Version object.</summary>
        public static Version Parse(string input) => new Version(input);

        public static bool operator ==(Version? v1, Version? v2) => v1 is null ? v2 is null : v1.Equals(v2);
        public static bool operator !=(Version? v1, Version? v2) => !(v1 == v2);
        public static bool operator <(Version? v1, Version? v2) => v1 is null ? v2 is not null : v1.CompareTo(v2) < 0;
        public static bool operator <=(Version? v1, Version? v2) => v1 is null || v1.CompareTo(v2) <= 0;
        public static bool operator >(Version? v1, Version? v2) => v1 is not null && v1.CompareTo(v2) > 0;
        public static bool operator >=(Version? v1, Version? v2) => v1 is null ? v2 is null : v1.CompareTo(v2) >= 0;
    }
}
