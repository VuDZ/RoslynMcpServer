using System.Diagnostics;

namespace BuildHostFork;

/// <summary>
/// Polls the BuildHost processes that start during a load every 100 ms. MSBuildWorkspace stops its BuildHost
/// processes when OpenSolutionAsync returns, so they must be observed while it runs. CPU is the last observed
/// value, a lower bound. A netcore BuildHost is a dotnet process with the BuildHost dll on its command line;
/// the .NET Framework one is its own executable. A dotnet process whose command line cannot be read is counted
/// as a BuildHost, so an unreadable host is not silently left out.
/// </summary>
internal sealed class BuildHostSampler : IDisposable
{
    /// <summary>Completes with the first BuildHost process seen.</summary>
    public Task<int> FirstHost => _firstHost.Task;

    public BuildHostSampler()
    {
        _since = DateTime.Now;
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    public object Stop()
    {
        _timer.Dispose();
        Sample();
        lock (_gate)
        {
            return new
            {
                count = _processes.Count,
                cpuMs = _processes.Values.Sum(process => process.CpuMs),
                peakWorkingSetMb = _processes.Count == 0 ? 0 : _processes.Values.Max(process => process.PeakWorkingSetBytes) / 1048576.0,
                hosts = _processes.Values.Select(process => process.Host).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            };
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the observed processes to exit, then kills the rest so a failed
    /// run does not leak them into the next one.
    /// </summary>
    /// <returns>The ids of the processes that were still running after the timeout.</returns>
    public async Task<int[]> WaitForExitAsync(TimeSpan timeout)
    {
        int[] ids;
        lock (_gate)
        {
            ids = _processes.Keys.ToArray();
        }

        var deadline = DateTime.UtcNow + timeout;
        var alive = ids.Where(IsRunning).ToArray();
        while (alive.Length > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100).ConfigureAwait(false);
            alive = alive.Where(IsRunning).ToArray();
        }

        foreach (var id in alive)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It exited after the last check.
            }
        }

        return alive;
    }

    public void Dispose() => _timer.Dispose();

    private static bool IsRunning(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void Sample()
    {
        var candidates = Process.GetProcessesByName("dotnet").Concat(Process.GetProcessesByName(FrameworkHostName));
        foreach (var candidate in candidates)
        {
            using (candidate)
            {
                try
                {
                    if (candidate.Id == Environment.ProcessId || candidate.StartTime < _since)
                    {
                        continue;
                    }

                    var host = Identify(candidate);
                    if (host is null)
                    {
                        continue;
                    }

                    var cpuMs = candidate.TotalProcessorTime.TotalMilliseconds;
                    var peak = candidate.PeakWorkingSet64;
                    lock (_gate)
                    {
                        var previous = _processes.GetValueOrDefault(candidate.Id);
                        _processes[candidate.Id] = (previous.Host ?? host, Math.Max(previous.CpuMs, cpuMs), Math.Max(previous.PeakWorkingSetBytes, peak));
                    }

                    _firstHost.TrySetResult(candidate.Id);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited between the listing and the read.
                }
            }
        }
    }

    // The folder of the BuildHost, which tells the package host from the fork, or null for another dotnet process.
    private static string? Identify(Process process)
    {
        if (process.ProcessName == FrameworkHostName)
        {
            return "net472: " + (Path.GetDirectoryName(process.MainModule?.FileName) ?? "(unreadable)");
        }

        var commandLine = ProcessCommandLine.TryRead(process);
        if (commandLine is null)
        {
            return "(unreadable command line)";
        }

        var end = commandLine.IndexOf(HostDll, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return null;
        }

        var quoted = end + HostDll.Length < commandLine.Length && commandLine[end + HostDll.Length] == '"';
        var start = commandLine.LastIndexOf(quoted ? '"' : ' ', end) + 1;
        return "netcore: " + commandLine[start..end].TrimEnd('/', '\\');
    }

    private const string FrameworkHostName = "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost";
    private const string HostDll = "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll";

    private readonly DateTime _since;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Host, double CpuMs, long PeakWorkingSetBytes)> _processes = [];
    private readonly TaskCompletionSource<int> _firstHost = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
