using System.Diagnostics;
using System.Runtime;

namespace ParallelSearch;

/// <summary>GC mode and memory the process ended with, so runs under different GC settings stay distinguishable.</summary>
internal static class GcReport
{
    /// <summary>
    /// The mode the runtime actually applied. Environment variables and runtimeconfig both select it,
    /// so the settings a script passed prove nothing on their own.
    /// </summary>
    public static object DescribeMode() => new
    {
        server = GCSettings.IsServerGC,
        latencyMode = GCSettings.LatencyMode.ToString(),
        configuration = GC.GetConfigurationVariables(),
    };

    /// <summary>
    /// Memory after both searches with no forced collection: what a long-lived server would keep holding.
    /// The process peak covers load and both searches, unlike the per-search sampler.
    /// </summary>
    public static object DescribeFinalMemory()
    {
        using var process = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();
        return new
        {
            workingSetMb = process.WorkingSet64 / 1048576.0,
            processPeakWorkingSetMb = process.PeakWorkingSet64 / 1048576.0,
            gcHeapMb = info.HeapSizeBytes / 1048576.0,
            gcCommittedMb = info.TotalCommittedBytes / 1048576.0,
        };
    }
}
