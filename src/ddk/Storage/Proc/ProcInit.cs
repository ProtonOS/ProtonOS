// ProtonOS DDK - Proc Filesystem Initialization
// Registers all proc entries and mounts the filesystem.

using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Storage.Proc.Generators;

namespace ProtonOS.DDK.Storage.Proc;

/// <summary>
/// Initializes and mounts the /proc filesystem.
/// </summary>
public static class ProcInit
{
    private static ProcFileSystem? _procFs;
    private static bool _initialized;

    /// <summary>
    /// Get the proc filesystem instance.
    /// </summary>
    public static ProcFileSystem? FileSystem => _procFs;

    /// <summary>
    /// Whether the proc filesystem has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initialize and mount the /proc filesystem.
    /// Should be called after VFS and NetworkManager are initialized.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        Debug.WriteLine("[ProcInit] Initializing /proc filesystem...");

        // Create filesystem instance
        _procFs = new ProcFileSystem();

        // Register system info generators
        _procFs.Register("/cpuinfo", new CpuInfoGenerator());
        _procFs.Register("/meminfo", new MemInfoGenerator());
        _procFs.Register("/stat", new StatGenerator());

        // Register network generators
        _procFs.Register("/net/dev", new NetDevGenerator());
        _procFs.Register("/net/arp", new NetArpGenerator());
        _procFs.Register("/net/tcp", new NetTcpGenerator());

        // Mount the filesystem at /proc
        var result = VFS.Mount("/proc", _procFs, null, readOnly: true);
        if (result != FileResult.Success)
        {
            Debug.Write("[ProcInit] Failed to mount /proc: ");
            Debug.WriteDecimal((uint)result);
            Debug.WriteLine();
            return;
        }

        _initialized = true;
        Debug.WriteLine("[ProcInit] /proc filesystem mounted successfully");
    }

    /// <summary>
    /// Register a custom proc entry.
    /// </summary>
    /// <param name="path">Path relative to /proc (e.g., "/myinfo").</param>
    /// <param name="generator">Content generator for this entry.</param>
    public static void Register(string path, IProcContentGenerator generator)
    {
        if (_procFs == null)
        {
            Debug.WriteLine("[ProcInit] Cannot register: proc filesystem not initialized");
            return;
        }

        _procFs.Register(path, generator);
    }
}
