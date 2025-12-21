// ProtonOS korlib - System.Threading.Thread
// Provides .NET-compatible Thread class wrapping kernel thread functionality.

namespace System
{
    /// <summary>
    /// Indicates that the value of a static field is unique for each thread.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class ThreadStaticAttribute : Attribute
    {
        public ThreadStaticAttribute() { }
    }
}

namespace System.Threading
{
    /// <summary>
    /// Thread state enumeration matching kernel ThreadState.
    /// </summary>
    public enum ThreadState
    {
        /// <summary>Thread has been created but not started.</summary>
        Unstarted = 0,
        /// <summary>Thread is running or ready to run.</summary>
        Running = 1,
        /// <summary>Thread is waiting/blocked.</summary>
        WaitSleepJoin = 2,
        /// <summary>Thread has been suspended.</summary>
        Suspended = 3,
        /// <summary>Thread has terminated.</summary>
        Stopped = 4,
        /// <summary>Thread was aborted.</summary>
        Aborted = 5,
        /// <summary>Thread is in background mode.</summary>
        Background = 6
    }

    /// <summary>
    /// Delegate for thread entry point without parameters.
    /// </summary>
    public delegate void ThreadStart();

    /// <summary>
    /// Delegate for thread entry point with a parameter.
    /// </summary>
    public delegate void ParameterizedThreadStart(object? obj);

    /// <summary>
    /// Represents a thread of execution.
    /// </summary>
    public sealed class Thread
    {
        private readonly ThreadStart? _start;
        private readonly ParameterizedThreadStart? _parameterizedStart;
        private readonly int _maxStackSize;
        private unsafe void* _nativeThread;
        private int _managedThreadId;
        private string? _name;
        private bool _isBackground;
        private volatile bool _started;
        private object? _startParameter;

        // Thread-local storage for current thread reference
        [ThreadStatic]
        private static Thread? t_currentThread;

        /// <summary>
        /// Initializes a new Thread with the specified entry point.
        /// </summary>
        public Thread(ThreadStart start)
        {
            _start = start ?? throw new ArgumentNullException(nameof(start));
            _parameterizedStart = null;
            _maxStackSize = 0;
        }

        /// <summary>
        /// Initializes a new Thread with the specified entry point and stack size.
        /// </summary>
        public Thread(ThreadStart start, int maxStackSize)
        {
            if (maxStackSize < 0)
                throw new ArgumentOutOfRangeException(nameof(maxStackSize));
            _start = start ?? throw new ArgumentNullException(nameof(start));
            _parameterizedStart = null;
            _maxStackSize = maxStackSize;
        }

        /// <summary>
        /// Initializes a new Thread with a parameterized entry point.
        /// </summary>
        public Thread(ParameterizedThreadStart start)
        {
            _parameterizedStart = start ?? throw new ArgumentNullException(nameof(start));
            _start = null;
            _maxStackSize = 0;
        }

        /// <summary>
        /// Initializes a new Thread with a parameterized entry point and stack size.
        /// </summary>
        public Thread(ParameterizedThreadStart start, int maxStackSize)
        {
            if (maxStackSize < 0)
                throw new ArgumentOutOfRangeException(nameof(maxStackSize));
            _parameterizedStart = start ?? throw new ArgumentNullException(nameof(start));
            _start = null;
            _maxStackSize = maxStackSize;
        }

        // Private constructor for CurrentThread
        private Thread(int managedThreadId)
        {
            _managedThreadId = managedThreadId;
            _started = true;
        }

        /// <summary>
        /// Internal factory method for AOT helpers to create a Thread representing a kernel thread.
        /// </summary>
        internal static Thread CreateForKernelThread(int managedThreadId)
        {
            return new Thread(managedThreadId);
        }

        /// <summary>
        /// Gets the unique identifier for the current managed thread.
        /// </summary>
        public int ManagedThreadId => _managedThreadId;

        /// <summary>
        /// Gets a value indicating whether the thread is alive.
        /// </summary>
        public unsafe bool IsAlive
        {
            get
            {
                if (!_started || _nativeThread == null)
                    return false;

                // Check kernel thread state
                int state = GetThreadStateNative(_nativeThread);
                // States 0-4 from kernel: Created=0, Ready=1, Running=2, Blocked=3, Suspended=4
                // Terminated=5 means not alive
                return state >= 0 && state < 5;
            }
        }

        /// <summary>
        /// Gets the state of the current thread.
        /// </summary>
        public unsafe ThreadState ThreadState
        {
            get
            {
                if (!_started)
                    return ThreadState.Unstarted;

                if (_nativeThread == null)
                    return ThreadState.Stopped;

                int kernelState = GetThreadStateNative(_nativeThread);
                return kernelState switch
                {
                    0 => ThreadState.Unstarted,  // Created
                    1 => ThreadState.Running,     // Ready
                    2 => ThreadState.Running,     // Running
                    3 => ThreadState.WaitSleepJoin, // Blocked
                    4 => ThreadState.Suspended,   // Suspended
                    5 => ThreadState.Stopped,     // Terminated
                    _ => ThreadState.Stopped
                };
            }
        }

        /// <summary>
        /// Gets or sets the name of the thread.
        /// </summary>
        public string? Name
        {
            get => _name;
            set => _name = value;
        }

        /// <summary>
        /// Gets or sets whether the thread is a background thread.
        /// </summary>
        public bool IsBackground
        {
            get => _isBackground;
            set => _isBackground = value;
        }

        /// <summary>
        /// Gets the currently running thread.
        /// </summary>
        public static Thread CurrentThread
        {
            get
            {
                if (t_currentThread == null)
                {
                    // Create a Thread object for the current kernel thread
                    uint threadId = GetCurrentThreadIdNative();
                    t_currentThread = new Thread((int)threadId);
                }
                return t_currentThread;
            }
        }

        /// <summary>
        /// Starts the thread.
        /// </summary>
        public void Start()
        {
            Start(null);
        }

        /// <summary>
        /// Starts the thread with a parameter.
        /// </summary>
        public unsafe void Start(object? parameter)
        {
            if (_started)
                throw new ThreadStateException("Thread has already been started.");

            _startParameter = parameter;
            _started = true;

            // Create the native thread
            _nativeThread = CreateThreadNative(
                &ThreadEntryPoint,
                null, // We'll use a different mechanism to pass 'this'
                (nuint)_maxStackSize,
                0, // flags
                out uint threadId);

            if (_nativeThread == null)
                throw new OutOfMemoryException("Failed to create thread.");

            _managedThreadId = (int)threadId;

            // Store this thread reference for the entry point to find
            // Note: In a real implementation, we'd need a thread-safe way to associate
            // the native thread with this Thread object. For now, we use a simple approach.
            SetPendingThread(this);
        }

        // Pending thread for thread startup
        private static Thread? s_pendingThread;
        private static readonly object s_pendingLock = new object();

        private static void SetPendingThread(Thread thread)
        {
            lock (s_pendingLock)
            {
                s_pendingThread = thread;
            }
        }

        private static Thread? GetAndClearPendingThread()
        {
            lock (s_pendingLock)
            {
                var thread = s_pendingThread;
                s_pendingThread = null;
                return thread;
            }
        }

        // Native thread entry point
        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static unsafe uint ThreadEntryPoint(void* parameter)
        {
            // Get the Thread object that was set for this thread
            var thread = GetAndClearPendingThread();
            if (thread == null)
                return 1; // Error

            // Set the current thread TLS
            t_currentThread = thread;

            try
            {
                if (thread._start != null)
                {
                    thread._start();
                }
                else if (thread._parameterizedStart != null)
                {
                    thread._parameterizedStart(thread._startParameter);
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// Blocks the calling thread until this thread terminates.
        /// </summary>
        public void Join()
        {
            Join(Timeout.Infinite);
        }

        /// <summary>
        /// Blocks the calling thread until this thread terminates or the timeout expires.
        /// </summary>
        /// <param name="millisecondsTimeout">Timeout in milliseconds (-1 = infinite).</param>
        /// <returns>true if the thread terminated; false if the timeout expired.</returns>
        public unsafe bool Join(int millisecondsTimeout)
        {
            if (!_started)
                throw new ThreadStateException("Thread has not been started.");

            if (_nativeThread == null)
                return true; // Already terminated

            // Poll the thread state until it terminates or timeout
            int elapsed = 0;
            const int pollInterval = 10;

            while (true)
            {
                int state = GetThreadStateNative(_nativeThread);
                if (state == 5) // Terminated
                    return true;

                if (millisecondsTimeout != Timeout.Infinite)
                {
                    if (elapsed >= millisecondsTimeout)
                        return false;
                }

                Sleep(pollInterval);
                elapsed += pollInterval;
            }
        }

        /// <summary>
        /// Suspends the current thread for the specified number of milliseconds.
        /// </summary>
        public static void Sleep(int millisecondsTimeout)
        {
            if (millisecondsTimeout < -1)
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

            if (millisecondsTimeout == -1)
            {
                // Infinite sleep - just sleep for a long time repeatedly
                while (true)
                    SleepNative(uint.MaxValue);
            }

            SleepNative((uint)millisecondsTimeout);
        }

        /// <summary>
        /// Suspends the current thread for the specified time span.
        /// </summary>
        public static void Sleep(TimeSpan timeout)
        {
            long ms = (long)timeout.TotalMilliseconds;
            if (ms < -1 || ms > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            Sleep((int)ms);
        }

        /// <summary>
        /// Causes the calling thread to yield execution to another thread.
        /// </summary>
        /// <returns>true if the operating system switched execution to another thread.</returns>
        public static bool Yield()
        {
            YieldNative();
            return true;
        }

        /// <summary>
        /// Causes the thread to wait the number of times defined by the iterations parameter.
        /// </summary>
        public static void SpinWait(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                // Just a tight loop - on x86 this could use PAUSE instruction
                // but for now a simple loop works
            }
        }

        /// <summary>
        /// Allocates an unnamed data slot on all threads.
        /// </summary>
        public static LocalDataStoreSlot AllocateDataSlot()
        {
            return new LocalDataStoreSlot();
        }

        /// <summary>
        /// Allocates a named data slot on all threads.
        /// </summary>
        public static LocalDataStoreSlot AllocateNamedDataSlot(string name)
        {
            return new LocalDataStoreSlot(name);
        }

        /// <summary>
        /// Gets data from the current thread's data slot.
        /// </summary>
        public static object? GetData(LocalDataStoreSlot slot)
        {
            return slot?.GetValue();
        }

        /// <summary>
        /// Sets data in the current thread's data slot.
        /// </summary>
        public static void SetData(LocalDataStoreSlot slot, object? data)
        {
            slot?.SetValue(data);
        }

        // Native method wrappers - these call the kernel exports
        // When built with the kernel (bflat), these are direct calls to Scheduler
        // When built as IL library (dotnet build), these are stubs

#if !KORLIB_IL
        private static unsafe void* CreateThreadNative(
            delegate* unmanaged<void*, uint> entryPoint,
            void* parameter,
            nuint stackSize,
            uint flags,
            out uint threadId)
        {
            return ProtonOS.Threading.Scheduler.CreateThread(entryPoint, parameter, stackSize, flags, out threadId);
        }

        private static uint GetCurrentThreadIdNative()
        {
            return ProtonOS.Threading.Scheduler.GetCurrentThreadId();
        }

        private static unsafe int GetThreadStateNative(void* thread)
        {
            var t = (ProtonOS.Threading.Thread*)thread;
            if (t == null) return -1;
            return (int)t->State;
        }

        private static void SleepNative(uint milliseconds)
        {
            ProtonOS.Threading.Scheduler.Sleep(milliseconds);
        }

        private static void YieldNative()
        {
            ProtonOS.Threading.Scheduler.Yield();
        }
#else
        // IL stubs - actual implementation provided by JIT resolution to kernel exports
        private static unsafe void* CreateThreadNative(
            delegate* unmanaged<void*, uint> entryPoint,
            void* parameter,
            nuint stackSize,
            uint flags,
            out uint threadId)
        {
            threadId = 0;
            throw new PlatformNotSupportedException();
        }

        private static uint GetCurrentThreadIdNative()
        {
            throw new PlatformNotSupportedException();
        }

        private static unsafe int GetThreadStateNative(void* thread)
        {
            throw new PlatformNotSupportedException();
        }

        private static void SleepNative(uint milliseconds)
        {
            throw new PlatformNotSupportedException();
        }

        private static void YieldNative()
        {
            throw new PlatformNotSupportedException();
        }
#endif
    }

    /// <summary>
    /// Encapsulates a slot for thread-local data.
    /// </summary>
    public sealed class LocalDataStoreSlot
    {
        private readonly string? _name;
        [ThreadStatic]
        private static System.Collections.Generic.Dictionary<int, object?>? t_data;
        private static int s_nextSlotId;
        private readonly int _slotId;

        internal LocalDataStoreSlot()
        {
            _slotId = Interlocked.Increment(ref s_nextSlotId);
        }

        internal LocalDataStoreSlot(string name) : this()
        {
            _name = name;
        }

        internal object? GetValue()
        {
            if (t_data == null)
                return null;
            t_data.TryGetValue(_slotId, out var value);
            return value;
        }

        internal void SetValue(object? value)
        {
            t_data ??= new System.Collections.Generic.Dictionary<int, object?>();
            t_data[_slotId] = value;
        }
    }

    /// <summary>
    /// Exception thrown when a thread operation fails due to invalid thread state.
    /// </summary>
    public class ThreadStateException : SystemException
    {
        public ThreadStateException() : base("Thread is in an invalid state for this operation.") { }
        public ThreadStateException(string? message) : base(message) { }
        public ThreadStateException(string? message, Exception? innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Contains constants for infinite timeout.
    /// </summary>
    public static class Timeout
    {
        /// <summary>
        /// Represents an infinite timeout (-1).
        /// </summary>
        public const int Infinite = -1;

        /// <summary>
        /// Represents an infinite timeout as TimeSpan.
        /// </summary>
        public static readonly TimeSpan InfiniteTimeSpan = new TimeSpan(0, 0, 0, 0, -1);
    }
}
