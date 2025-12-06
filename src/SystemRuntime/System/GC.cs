// ProtonOS System.Runtime - GC
// Controls the system garbage collector.

namespace System
{
    /// <summary>
    /// Controls the system garbage collector, a service that automatically reclaims unused memory.
    /// </summary>
    public static class GC
    {
        /// <summary>Requests that the system not call the finalizer for the specified object.</summary>
        public static void SuppressFinalize(object obj)
        {
            // In a full GC implementation, this would mark the object to skip finalization
            // For now, this is a no-op since our GC doesn't support finalization
            if (obj == null) throw new ArgumentNullException(nameof(obj));
        }

        /// <summary>Requests that the common language runtime call the finalizer for the specified object.</summary>
        public static void ReRegisterForFinalize(object obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
        }

        /// <summary>Forces an immediate garbage collection of all generations.</summary>
        public static void Collect()
        {
            // Would trigger GC in kernel
        }

        /// <summary>Forces an immediate garbage collection from generation 0 through a specified generation.</summary>
        public static void Collect(int generation)
        {
            // Would trigger GC in kernel
        }

        /// <summary>Returns the current generation number of the specified object.</summary>
        public static int GetGeneration(object obj)
        {
            // Simplified - always return 0
            return 0;
        }

        /// <summary>Returns the number of bytes currently thought to be allocated.</summary>
        public static long GetTotalMemory(bool forceFullCollection)
        {
            // Would return actual memory from kernel
            return 0;
        }

        /// <summary>Retrieves the number of times garbage collection has occurred for the specified generation of objects.</summary>
        public static int CollectionCount(int generation)
        {
            return 0;
        }

        /// <summary>Informs the runtime of a large allocation of unmanaged memory that should be taken into account when scheduling garbage collection.</summary>
        public static void AddMemoryPressure(long bytesAllocated)
        {
            if (bytesAllocated <= 0) throw new ArgumentOutOfRangeException(nameof(bytesAllocated));
        }

        /// <summary>Informs the runtime that unmanaged memory has been released.</summary>
        public static void RemoveMemoryPressure(long bytesAllocated)
        {
            if (bytesAllocated <= 0) throw new ArgumentOutOfRangeException(nameof(bytesAllocated));
        }

        /// <summary>Suspends the current thread until the thread that is processing the queue of finalizers has emptied that queue.</summary>
        public static void WaitForPendingFinalizers()
        {
            // Would wait for finalizer thread in kernel
        }

        /// <summary>Returns the maximum number of generations that the system currently supports.</summary>
        public static int MaxGeneration => 2;

        /// <summary>Allocates a block of memory.</summary>
        public static T[] AllocateUninitializedArray<T>(int length)
        {
            return new T[length];
        }
    }
}
