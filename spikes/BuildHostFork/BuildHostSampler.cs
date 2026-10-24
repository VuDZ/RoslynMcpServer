using System.Diagnostics;

namespace BuildHostFork;

/// <summary>
/// Polls the BuildHost processes that this process starts during a load, every 100 ms. MSBuildWorkspace stops its
/// BuildHost processes when OpenSolutionAsync returns, so they must be observed while it runs. CPU is the last
/// observed value, a lower bound, and a process that starts and exits between two polls is not seen at all.
/// A netcore BuildHost is a dotnet process with the BuildHost dll on its command line; the .NET Framework one is
/// its own executable.
/// </summary>
/// <remarks>
/// Only descendants of this process count: another IDE or MCP server on the machine may load a solution at the
/// same time, and its BuildHost must neither be measured nor killed. A descendant dotnet process whose command
/// line cannot be read is reported as unidentified rather than counted as a BuildHost, so an unreadable process
/// is neither hidden nor taken for the host.
/// </remarks>
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
        // Timer.Dispose() alone does not wait for a poll already running, which could still add a process.
        using (var stopped = new ManualResetEvent(false))
        {
            if (_timer.Dispose(stopped))
            {
                stopped.WaitOne();
            }
        }

        Sample();
        lock (_gate)
        {
            return new
            {
                count = _processes.Count,
                cpuMs = _processes.Values.Sum(process => process.CpuMs),
                // Of the largest single BuildHost, not of all of them together or of their MSBuild nodes.
                peakWorkingSetMb = _processes.Count == 0 ? 0 : _processes.Values.Max(process => process.PeakWorkingSetBytes) / 1048576.0,
                hosts = _processes.Values.Select(process => process.Host).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                unidentified = _unidentified.Count,
            };
        }
    }

    /// <summary>Kills a BuildHost seen by this sampler, unless its id now belongs to another process.</summary>
    /// <returns>False when the process had already exited.</returns>
    public bool Kill(int id)
    {
        using var process = TryOpen(id);
        if (process is null)
        {
            return false;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the observed processes to exit, then kills the rest so a failed
    /// run does not leak them into the next one.
    /// </summary>
    /// <returns>The ids of the processes that were still running after the timeout, counted before the kill.</returns>
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
            Kill(id);
        }

        return alive;
    }

    public void Dispose() => _timer.Dispose();

    private bool IsRunning(int id)
    {
        using var process = TryOpen(id);
        try
        {
            return process is not null && !process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    // The observed process with this id, or null when it exited and the id is free or was given to another process.
    private Process? TryOpen(int id)
    {
        DateTime startTime;
        lock (_gate)
        {
            if (!_processes.TryGetValue(id, out var observed))
            {
                return null;
            }

            startTime = observed.StartTime;
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(id);
            if ((process.StartTime - startTime).Duration() <= _startTimeTolerance)
            {
                return process;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It exited.
        }

        process?.Dispose();
        return null;
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
                    var startTime = candidate.StartTime;
                    if (candidate.Id == Environment.ProcessId || startTime < _since || !IsDescendant(candidate, startTime))
                    {
                        continue;
                    }

                    var host = Identify(candidate);
                    if (host is null)
                    {
                        continue;
                    }

                    if (host == Unidentified)
                    {
                        // A process that exited between the listing and the read has no command line either; it is
                        // gone, not unknown, and must not fail the run.
                        if (HasExited(candidate))
                        {
                            continue;
                        }

                        lock (_gate)
                        {
                            _unidentified.Add(candidate.Id);
                        }

                        continue;
                    }

                    var cpuMs = candidate.TotalProcessorTime.TotalMilliseconds;
                    var peak = candidate.PeakWorkingSet64;
                    lock (_gate)
                    {
                        _unidentified.Remove(candidate.Id);
                        var previous = _processes.GetValueOrDefault(candidate.Id);
                        _processes[candidate.Id] = (previous.Host ?? host, startTime, Math.Max(previous.CpuMs, cpuMs), Math.Max(previous.PeakWorkingSetBytes, peak));
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

    // The client starts BuildHost directly; a few levels up allow for a dotnet launcher in between. A parent that
    // started after its child is not the real parent: the real one exited and its id was reused.
    private static bool IsDescendant(Process process, DateTime startTime)
    {
        var childStart = startTime;
        var parentId = ProcessQuery.TryReadParentId(process);
        for (var depth = 0; depth < MaxAncestorDepth && parentId is int id; depth++)
        {
            if (id == Environment.ProcessId)
            {
                return true;
            }

            try
            {
                using var parent = Process.GetProcessById(id);
                if (parent.StartTime > childStart + _startTimeTolerance)
                {
                    return false;
                }

                childStart = parent.StartTime;
                parentId = ProcessQuery.TryReadParentId(parent);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        return false;
    }

    // Access denied means the process is alive but protected, which is exactly what must stay unidentified.
    private static bool HasExited(Process process)
    {
        try
        {
            process.Refresh();
            return process.HasExited;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    // The folder of the BuildHost, which tells the package host from the fork; Unidentified for a dotnet process
    // whose command line cannot be read; null for another dotnet process, such as an MSBuild node.
    private static string? Identify(Process process)
    {
        if (process.ProcessName == FrameworkHostName)
        {
            return "net472: " + (Path.GetDirectoryName(process.MainModule?.FileName) ?? "(unreadable)");
        }

        var commandLine = ProcessQuery.TryReadCommandLine(process);
        if (commandLine is null)
        {
            return Unidentified;
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
    private const string Unidentified = "(unidentified)";
    private const int MaxAncestorDepth = 3;

    // The start time tells a process from a newer one that reused its id. Windows reports it exactly. On Linux
    // Process.StartTime is the boot time plus a start tick of 10 ms, recomputed on every read, so it may move by a
    // few milliseconds; 100 ms covers that while a reused id within it needs the whole pid range to wrap.
    private static readonly TimeSpan _startTimeTolerance = OperatingSystem.IsWindows() ? TimeSpan.Zero : TimeSpan.FromMilliseconds(100);

    private readonly DateTime _since;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Host, DateTime StartTime, double CpuMs, long PeakWorkingSetBytes)> _processes = [];
    private readonly HashSet<int> _unidentified = [];
    private readonly TaskCompletionSource<int> _firstHost = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
