namespace System
{
    public struct Nullable<T> where T : struct
    {
        private readonly bool _hasValue;
        private T _value;

        public Nullable(T value) => (_hasValue, _value) = (true, value);

        public readonly bool HasValue => _hasValue;

        public readonly T Value
        {
            get
            {
                // Note: Normally this would throw InvalidOperationException if !_hasValue
                // For now we return default value to avoid cross-assembly FailFast issues
                return _value;
            }
        }

        public readonly T GetValueOrDefault() => _value;

        public readonly T GetValueOrDefault(T defaultValue) => _hasValue ? _value : defaultValue;

        public static implicit operator Nullable<T>(T value) => new Nullable<T>(value);

        public static explicit operator T(Nullable<T> value) => value.Value;
    }
}
