// ProtonOS korlib - CultureInfo stub
// Minimal stub for culture info - required by reflection APIs.

namespace System.Globalization
{
    /// <summary>
    /// Provides information about a specific culture.
    /// This is a minimal stub for reflection support.
    /// </summary>
    public class CultureInfo
    {
        private static readonly CultureInfo _invariantCulture = new CultureInfo("", false);
        private static readonly CultureInfo _currentCulture = _invariantCulture;
        private static readonly CultureInfo _currentUICulture = _invariantCulture;

        private readonly string _name;
        private readonly bool _useUserOverride;

        public CultureInfo(string name)
            : this(name, true)
        {
        }

        public CultureInfo(string name, bool useUserOverride)
        {
            _name = name ?? string.Empty;
            _useUserOverride = useUserOverride;
        }

        public static CultureInfo InvariantCulture => _invariantCulture;
        public static CultureInfo CurrentCulture => _currentCulture;
        public static CultureInfo CurrentUICulture => _currentUICulture;

        public string Name => _name;
        public bool UseUserOverride => _useUserOverride;

        public override string ToString() => _name;
    }
}
