// ProtonOS DDK - /proc/stat Generator
// Generates system statistics in Linux-compatible format.

using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Storage.Proc.Generators;

/// <summary>
/// Generates content for /proc/stat.
/// Reports system statistics from kernel exports.
/// </summary>
public class StatGenerator : IProcContentGenerator
{
    /// <inheritdoc/>
    public string Generate()
    {
        string result = "";

        // CPU line (aggregate stats - we don't have detailed time accounting yet)
        // Format: cpu user nice system idle iowait irq softirq steal guest guest_nice
        result = result + "cpu  0 0 0 0 0 0 0 0 0 0\n";

        // Per-CPU stats
        int cpuCount = CPU.GetCpuCount();
        for (int i = 0; i < cpuCount; i++)
        {
            string line = "cpu";
            line = line + i.ToString();
            line = line + " 0 0 0 0 0 0 0 0 0 0\n";
            result = result + line;
        }

        // Get scheduler stats
        int running, blocked;
        ulong ctxtSwitches;
        Thread.GetSchedulerStats(out running, out blocked, out ctxtSwitches);

        // Context switches
        string ctxt = "ctxt ";
        ctxt = ctxt + ctxtSwitches.ToString();
        result = result + ctxt + "\n";

        // Boot time (we don't track this, use 0)
        result = result + "btime 0\n";

        // Process count (we track threads)
        string proc = "processes ";
        proc = proc + Thread.GetThreadCount().ToString();
        result = result + proc + "\n";

        string run = "procs_running ";
        run = run + running.ToString();
        result = result + run + "\n";

        string block = "procs_blocked ";
        block = block + blocked.ToString();
        result = result + block + "\n";

        return result;
    }
}
