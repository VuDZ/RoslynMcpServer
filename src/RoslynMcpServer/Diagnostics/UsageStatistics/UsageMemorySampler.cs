using System.Diagnostics;
using System.Globalization;
using RoslynMcpServer.Diagnostics.UsageStatistics.Models;

namespace RoslynMcpServer.Diagnostics.UsageStatistics;

internal static class UsageMemorySampler
{
    internal static UsageMemorySnapshot Capture()
    {
        long? memory = null;
        long? workingSet = null;
        long? heap = null;
        long? committed = null;
        long? childMemory = null;
        int? childCount = null;
        try
        {
            using var self = Process.GetCurrentProcess();
            memory = ReadPrimaryMemory(self);
            workingSet = self.WorkingSet64;
            var gc = GC.GetGCMemoryInfo();
            heap = gc.HeapSizeBytes;
            committed = gc.TotalCommittedBytes;
        }
        catch (Exception ex) when (IsProbeFailure(ex))
        {
            // Missing counters remain unknown, never zero memory.
        }

        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var plan = UnregisteredBuildHostReaper.CaptureChildProcesses();
                (childMemory, childCount) = BuildHostTotals(plan, plan.ProcessIds.Select(ReadProcessMemory));
            }
        }
        catch (Exception ex) when (IsProbeFailure(ex))
        {
            childMemory = null;
        }

        return new UsageMemorySnapshot(memory, childMemory, childCount, workingSet, heap, committed);
    }

    internal static long? ParseLinuxPrivateMemory(string status)
    {
        long? anonymous = null;
        long? swap = null;
        foreach (var line in status.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kibibytes))
            {
                continue;
            }

            if (parts[0] == "RssAnon:")
            {
                anonymous = checked(kibibytes * 1024);
            }
            else if (parts[0] == "VmSwap:")
            {
                swap = checked(kibibytes * 1024);
            }
        }

        return anonymous + swap;
    }

    internal static (long? Bytes, int? Count) BuildHostTotals(UnregisteredBuildHostReapPlan plan, IEnumerable<long?> sizes)
    {
        if (plan.UnidentifiedChildren > 0)
        {
            return (null, null);
        }

        long? total = 0;
        foreach (var size in sizes)
        {
            total += size;
        }

        return (total, plan.ProcessIds.Count);
    }

    private static long? ReadProcessMemory(int processId)
    {
        using var process = Process.GetProcessById(processId);
        return ReadPrimaryMemory(process);
    }

    private static long? ReadPrimaryMemory(Process process) =>
        OperatingSystem.IsLinux()
            ? ParseLinuxPrivateMemory(File.ReadAllText($"/proc/{process.Id}/status"))
            : process.PrivateMemorySize64;

    private static bool IsProbeFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.ComponentModel.Win32Exception or OverflowException;
}
