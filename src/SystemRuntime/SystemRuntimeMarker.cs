// ProtonOS System.Runtime Assembly
// This assembly provides runtime library types for JIT-compiled code.

namespace System.Runtime
{
    /// <summary>
    /// Marker class indicating this is the ProtonOS System.Runtime assembly.
    /// </summary>
    internal static class SystemRuntimeMarker
    {
        internal const string Version = "1.0.0";
        internal const string Description = "ProtonOS Runtime Library";
    }

    /// <summary>
    /// Large struct (32 bytes) for testing cross-assembly large struct return.
    /// This is exactly the same size as DMABuffer (4 × ulong = 32 bytes).
    /// </summary>
    public struct LargeTestStruct
    {
        public ulong A;  // offset 0
        public ulong B;  // offset 8
        public ulong C;  // offset 16
        public ulong D;  // offset 24
    }

    /// <summary>
    /// Helper class for cross-assembly large struct return testing.
    /// </summary>
    public static class StructHelper
    {
        /// <summary>
        /// Creates and returns a large struct (32 bytes).
        /// Uses hidden buffer return convention (>16 bytes).
        /// </summary>
        public static LargeTestStruct CreateLargeStruct(ulong a, ulong b, ulong c, ulong d)
        {
            LargeTestStruct result;
            result.A = a;
            result.B = b;
            result.C = c;
            result.D = d;
            return result;
        }
    }
}
