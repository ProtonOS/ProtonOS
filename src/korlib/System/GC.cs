// ProtonOS korlib - GC
// Garbage collection control and information.
// Minimal implementation for bare-metal environment.

namespace System
{
    /// <summary>
    /// Controls the system garbage collector, a service that automatically reclaims unused memory.
    /// </summary>
    public static class GC
    {
        /// <summary>
        /// Requests that the common language runtime not call the finalizer for the specified object.
        /// </summary>
        /// <remarks>
        /// In the ProtonOS bare-metal environment, finalizers are not automatically called,
        /// so this method is a no-op.
        /// </remarks>
        public static void SuppressFinalize(object obj)
        {
            // No-op in bare-metal environment
            // Finalizers are not automatically invoked
        }

        /// <summary>
        /// Requests that the system call the finalizer for the specified object for which SuppressFinalize has previously been called.
        /// </summary>
        public static void ReRegisterForFinalize(object obj)
        {
            // No-op in bare-metal environment
        }

        /// <summary>
        /// Forces an immediate garbage collection of all generations.
        /// </summary>
        public static void Collect()
        {
            // TODO: Hook into kernel GC when available
        }

        /// <summary>
        /// Forces an immediate garbage collection from generation 0 through a specified generation.
        /// </summary>
        public static void Collect(int generation)
        {
            // TODO: Hook into kernel GC when available
        }

        /// <summary>
        /// Forces an immediate garbage collection from generation 0 through a specified generation.
        /// </summary>
        public static void Collect(int generation, GCCollectionMode mode)
        {
            // TODO: Hook into kernel GC when available
        }

        /// <summary>
        /// Returns the current generation number of the specified object.
        /// </summary>
        public static int GetGeneration(object obj)
        {
            return 0; // Single generation in simple GC
        }

        /// <summary>
        /// Gets the maximum number of generations the system currently supports.
        /// </summary>
        public static int MaxGeneration => 0;

        /// <summary>
        /// Informs the runtime of a large allocation of unmanaged memory that should be taken into account when scheduling garbage collection.
        /// </summary>
        public static void AddMemoryPressure(long bytesAllocated)
        {
            // TODO: Hook into kernel GC when available
        }

        /// <summary>
        /// Informs the runtime that unmanaged memory has been released and no longer needs to be taken into account when scheduling garbage collection.
        /// </summary>
        public static void RemoveMemoryPressure(long bytesAllocated)
        {
            // TODO: Hook into kernel GC when available
        }

        /// <summary>
        /// References the specified object, making it ineligible for garbage collection from the start of the current routine to the point where this method is called.
        /// </summary>
        public static void KeepAlive(object? obj)
        {
            // Compiler intrinsic - the call itself keeps the object alive
        }
    }

    /// <summary>
    /// Specifies the behavior for a forced garbage collection.
    /// </summary>
    public enum GCCollectionMode
    {
        /// <summary>The default setting for this enumeration.</summary>
        Default = 0,
        /// <summary>Forces the garbage collection to occur immediately.</summary>
        Forced = 1,
        /// <summary>Allows the garbage collector to determine whether the current time is optimal to reclaim objects.</summary>
        Optimized = 2,
        /// <summary>Requests that the garbage collector decommit as much memory as possible.</summary>
        Aggressive = 3
    }
}
