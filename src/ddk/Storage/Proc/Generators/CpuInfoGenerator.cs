// ProtonOS DDK - /proc/cpuinfo Generator
// Generates CPU topology information in Linux-compatible format.

using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Storage.Proc.Generators;

/// <summary>
/// Generates content for /proc/cpuinfo.
/// Reports CPU topology from kernel exports.
/// </summary>
public unsafe class CpuInfoGenerator : IProcContentGenerator
{
    /// <inheritdoc/>
    public string Generate()
    {
        string result = "";

        int cpuCount = CPU.GetCpuCount();
        CpuInfo info;

        for (int i = 0; i < cpuCount; i++)
        {
            if (!CPU.GetCpuInfo(i, &info))
                continue;

            // Build each line piece by piece to avoid 4-arg String.Concat
            string line = "processor\t: ";
            line = line + info.CpuIndex.ToString();
            result = result + line + "\n";

            line = "apicid\t\t: ";
            line = line + info.ApicId.ToString();
            result = result + line + "\n";

            line = "initial apicid\t: ";
            line = line + info.ApicId.ToString();
            result = result + line + "\n";

            line = "physical id\t: ";
            line = line + info.NumaNode.ToString();
            result = result + line + "\n";

            line = "core id\t\t: ";
            line = line + info.CpuIndex.ToString();
            result = result + line + "\n";

            line = "cpu cores\t: ";
            line = line + cpuCount.ToString();
            result = result + line + "\n";

            string flags = info.IsBsp ? "flags\t\t: bsp" : "flags\t\t:";

            if (info.IsOnline)
                flags = flags + " online";

            if (!info.IsEnabled)
                flags = flags + " disabled";

            result = result + flags;
            result = result + "\n\n";
        }

        return result;
    }
}
