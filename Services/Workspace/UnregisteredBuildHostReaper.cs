using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Terminates BuildHost processes that this process started and that
/// <c>Microsoft.CodeAnalysis.Workspaces.MSBuild</c> 5.9.0 did not register.
/// </summary>
/// <remarks>
/// <c>BuildHostProcessManager.NoLock_GetBuildHostAsync</c> starts the process, then calls
/// <c>ConfigureGlobalStateAsync</c> and <c>FindBestMSBuildAsync</c> with the load's cancellation token,
/// and only then adds the process to <c>_processes</c>. Cancellation in that window skips the add.
/// The manager's dispose shuts down registered processes only, so the unregistered one stays a child
/// of this server. Package commit <c>2624f5c156</c> kills the process on any initialization exception;
/// that commit is not in 5.9.0.
/// A host whose id is in the in-use set is left alone, as is a host that is not a descendant of this
/// process. Another IDE on the machine can load a solution at the same time. A descendant whose
/// command line cannot be read is not treated as a BuildHost. On macOS the parent and the command
/// line are not read, so nothing is killed.
/// </remarks>
internal static class UnregisteredBuildHostReaper
{
    /// <summary>
    /// Terminates descendant BuildHosts of this process that are not in <paramref name="inUseProcessIds"/>.
    /// </summary>
    internal static void Reap(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _ = Reap(logger, inUseProcessIds: null, limitToProcessIds: null);
    }

    /// <summary>
    /// Same as <see cref="Reap(ILogger)"/>, restricted to <paramref name="limitToProcessIds"/> when that set
    /// is not null. The restriction keeps a test from terminating a BuildHost that another process on the
    /// machine still has loaded.
    /// </summary>
    internal static UnregisteredBuildHostReapPlan Reap(
        ILogger logger,
        IReadOnlySet<int>? inUseProcessIds,
        IReadOnlySet<int>? limitToProcessIds)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Dictionary<int, BuildHostProcessObservation> processes;
        try
        {
            processes = Capture();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(ex, "Cancelled workspace load: BuildHost processes could not be listed.");
            return new UnregisteredBuildHostReapPlan([], 0);
        }

        var plan = Select(
            processes,
            inUseProcessIds ?? NoProcessIds,
            Environment.ProcessId,
            StartTimeTolerance,
            limitToProcessIds);
        var killed = new List<int>(plan.ProcessIds.Count);
        foreach (var id in plan.ProcessIds)
        {
            if (processes.TryGetValue(id, out var observation) && TryTerminate(observation))
            {
                killed.Add(id);
            }
        }

        if (killed.Count > 0)
        {
            logger.LogInformation(
                "Terminated {Count} BuildHost process(es) left by a cancelled workspace load: {ProcessIds}",
                killed.Count,
                string.Join(", ", killed));
        }

        if (plan.UnidentifiedDescendants > 0)
        {
            logger.LogWarning(
                "Cancelled workspace load: {Count} descendant process(es) had no readable command line and were left running.",
                plan.UnidentifiedDescendants);
        }

        return new UnregisteredBuildHostReapPlan(killed, plan.UnidentifiedDescendants);
    }

    internal static UnregisteredBuildHostReapPlan Select(
        IReadOnlyDictionary<int, BuildHostProcessObservation> processes,
        IReadOnlySet<int> inUseProcessIds,
        int ownProcessId,
        TimeSpan startTimeTolerance,
        IReadOnlySet<int>? limitToProcessIds = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(inUseProcessIds);

        var ids = new List<int>();
        var unidentified = 0;
        foreach (var process in processes.Values)
        {
            if (process.Id == ownProcessId || process.HasExited)
            {
                continue;
            }

            if (limitToProcessIds is not null && !limitToProcessIds.Contains(process.Id))
            {
                continue;
            }

            if (inUseProcessIds.Contains(process.Id))
            {
                continue;
            }

            if (!IsDescendant(process, processes, ownProcessId, startTimeTolerance))
            {
                continue;
            }

            if (IsBuildHost(process))
            {
                ids.Add(process.Id);
                continue;
            }

            if (IsUnidentifiedCandidate(process))
            {
                unidentified++;
            }
        }

        return new UnregisteredBuildHostReapPlan(ids, unidentified);
    }

    private static Dictionary<int, BuildHostProcessObservation> Capture()
    {
        var map = new Dictionary<int, BuildHostProcessObservation>();
        foreach (var name in CandidateProcessNames)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in found)
            {
                using (process)
                {
                    if (!TryObserve(process, out var observation) || !map.TryAdd(observation.Id, observation))
                    {
                        continue;
                    }

                    IncludeAncestors(map, observation);
                }
            }
        }

        return map;
    }

    private static void IncludeAncestors(
        Dictionary<int, BuildHostProcessObservation> map,
        BuildHostProcessObservation child)
    {
        var childStart = child.StartTime;
        var parentId = child.ParentId;
        for (var depth = 0; depth < MaxAncestorDepth && parentId is int id; depth++)
        {
            if (id == Environment.ProcessId)
            {
                return;
            }

            if (map.TryGetValue(id, out var existing))
            {
                if (existing.StartTime > childStart + StartTimeTolerance)
                {
                    return;
                }

                childStart = existing.StartTime;
                parentId = existing.ParentId;
                continue;
            }

            Process? parent = null;
            try
            {
                parent = Process.GetProcessById(id);
                if (!TryObserve(parent, out var observed)
                    || observed.StartTime > childStart + StartTimeTolerance)
                {
                    return;
                }

                map[id] = observed;
                childStart = observed.StartTime;
                parentId = observed.ParentId;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return;
            }
            finally
            {
                parent?.Dispose();
            }
        }
    }

    private static bool TryObserve(Process process, out BuildHostProcessObservation observation)
    {
        observation = default;
        try
        {
            if (process.Id == Environment.ProcessId)
            {
                return false;
            }

            var startTime = process.StartTime;
            var name = process.ProcessName;
            string? commandLine = null;
            if (NeedsCommandLine(name))
            {
                commandLine = TryReadCommandLine(process.Id);
            }

            observation = new BuildHostProcessObservation(
                process.Id,
                TryReadParentId(process),
                startTime,
                name,
                commandLine,
                process.HasExited);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryTerminate(BuildHostProcessObservation observation)
    {
        try
        {
            using var process = Process.GetProcessById(observation.Id);
            if ((process.StartTime - observation.StartTime).Duration() > StartTimeTolerance)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool IsBuildHost(BuildHostProcessObservation process)
    {
        if (process.ProcessName.Equals(FrameworkHostProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ContainsHostAssembly(process.CommandLine);
    }

    private static bool IsUnidentifiedCandidate(BuildHostProcessObservation process)
    {
        if (process.CommandLine is not null || IsBuildHost(process))
        {
            return false;
        }

        return process.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("mono", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHostAssembly(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return false;
        }

        return commandLine.Contains(NetCoreHostAssembly, StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains(FrameworkHostAssembly, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDescendant(
        BuildHostProcessObservation process,
        IReadOnlyDictionary<int, BuildHostProcessObservation> processes,
        int ownProcessId,
        TimeSpan startTimeTolerance)
    {
        var childStart = process.StartTime;
        var parentId = process.ParentId;
        for (var depth = 0; depth < MaxAncestorDepth && parentId is int id; depth++)
        {
            if (id == ownProcessId)
            {
                return true;
            }

            if (!processes.TryGetValue(id, out var parent))
            {
                return false;
            }

            if (parent.StartTime > childStart + startTimeTolerance)
            {
                return false;
            }

            childStart = parent.StartTime;
            parentId = parent.ParentId;
        }

        return false;
    }

    private static bool NeedsCommandLine(string processName)
        => processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("mono", StringComparison.OrdinalIgnoreCase);

    private static int? TryReadParentId(Process process)
    {
        var parentId = 0;
        if (OperatingSystem.IsWindows())
        {
            parentId = WindowsParentProcess.TryReadParentProcessId(process);
        }
        else if (OperatingSystem.IsLinux())
        {
            parentId = LinuxParentProcess.TryGetParentProcessId(process.Id);
        }

        return parentId > 0 ? parentId : null;
    }

    private static string? TryReadCommandLine(int processId)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsParentProcess.TryReadCommandLine(processId);
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxParentProcess.TryReadCommandLine(processId);
        }

        return null;
    }

    internal const string NetCoreHostAssembly = "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll";

    internal const string FrameworkHostAssembly = "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe";

    internal const string FrameworkHostProcessName = "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost";

    private const int MaxAncestorDepth = 3;

    private static readonly string[] CandidateProcessNames =
    [
        "dotnet",
        FrameworkHostProcessName,
        "mono",
    ];

    private static readonly HashSet<int> NoProcessIds = [];

    // Windows reports start time exactly. On Linux Process.StartTime is recomputed from a 10 ms tick and can
    // move by a few milliseconds between reads; 100 ms covers that. A reused id inside the window would have
    // to wrap the whole pid range.
    private static readonly TimeSpan StartTimeTolerance = OperatingSystem.IsWindows()
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(100);
}
