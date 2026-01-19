// ProtonOS DDK - /proc/meminfo Generator
// Generates memory statistics in Linux-compatible format.

using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Storage.Proc.Generators;

/// <summary>
/// Generates content for /proc/meminfo.
/// Reports memory statistics from kernel exports.
/// </summary>
public unsafe class MemInfoGenerator : IProcContentGenerator
{
    /// <inheritdoc/>
    public string Generate()
    {
        string result = "";

        // Get memory stats from kernel
        MemoryStats stats;
        bool hasStats = Memory.GetMemoryStats(&stats);

        if (hasStats)
        {
            ulong totalKb = stats.TotalMemory / 1024;
            ulong freeKb = stats.FreeMemory / 1024;
            ulong usedKb = totalKb - freeKb;

            result = AppendLine(result, "MemTotal:       ", totalKb, " kB");
            result = AppendLine(result, "MemFree:        ", freeKb, " kB");
            result = AppendLine(result, "MemUsed:        ", usedKb, " kB");

            // GC Heap statistics
            result = AppendLine(result, "GCHeapAlloc:    ", stats.GCHeapAllocated / 1024, " kB");
            result = AppendLine(result, "GCHeapFree:     ", stats.GCHeapFreeSpace / 1024, " kB");
            result = AppendLine(result, "GCObjects:      ", stats.GCHeapObjects, "");

            // Free list stats
            result = AppendLine(result, "GCFreeListKB:   ", stats.GCFreeListBytes / 1024, " kB");
            result = AppendLine(result, "GCFreeListCnt:  ", stats.GCFreeListCount, "");

            // LOH stats
            result = AppendLine(result, "LOHAlloc:       ", stats.LOHAllocated / 1024, " kB");
            result = AppendLine(result, "LOHObjects:     ", stats.LOHObjects, "");

            // Page allocator stats
            result = AppendLine(result, "PageTotal:      ", stats.TotalPages, "");
            result = AppendLine(result, "PageFree:       ", stats.FreePages, "");
        }
        else
        {
            result = result + "MemTotal:              0 kB\n";
            result = result + "MemFree:               0 kB\n";
        }

        return result;
    }

    /// <summary>
    /// Append a formatted line to result (avoids 4-arg String.Concat).
    /// </summary>
    private static string AppendLine(string result, string label, ulong value, string suffix)
    {
        string padded = PadLeft(value, 8);
        string line = label + padded;
        line = line + suffix;
        line = line + "\n";
        return result + line;
    }

    /// <summary>
    /// Pad a number with leading spaces to the given width.
    /// </summary>
    private static string PadLeft(ulong value, int width)
    {
        string str = value.ToString();
        while (str.Length < width)
            str = " " + str;
        return str;
    }
}
