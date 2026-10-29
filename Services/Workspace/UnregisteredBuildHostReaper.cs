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
/// Only direct children are candidates: 5.9.0 starts every host (dotnet, the net472 exe, mono) from
/// this process itself. A BuildHost deeper in the tree belongs to a program this server started for a
/// tool, such as <c>dotnet run</c> or <c>dotnet test</c>, and that program may still be using it.
/// A host of another process is left alone too: another IDE on the machine can load a solution at the
/// same time. A host whose id is in the in-use set is left alone, and a child whose command line cannot
/// be read is not treated as a BuildHost. On macOS the parent and the command line are not read, so
/// nothing is killed.
/// </remarks>
internal static class UnregisteredBuildHostReaper
{
    /// <summary>
    /// Terminates child BuildHosts of this process that are not in the in-use set.
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
        DateTime ownStartTime;
        try
        {
            processes = Capture();
            using var self = Process.GetCurrentProcess();
            ownStartTime = self.StartTime;
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
            ownStartTime,
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

        if (plan.UnidentifiedChildren > 0)
        {
            logger.LogWarning(
                "Cancelled workspace load: {Count} child process(es) had no readable command line and were left running.",
                plan.UnidentifiedChildren);
        }

        return new UnregisteredBuildHostReapPlan(killed, plan.UnidentifiedChildren);
    }

    internal static UnregisteredBuildHostReapPlan Select(
        IReadOnlyDictionary<int, BuildHostProcessObservation> processes,
        IReadOnlySet<int> inUseProcessIds,
        int ownProcessId,
        DateTime ownStartTime,
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

            if (!IsChild(process, ownProcessId, ownStartTime, startTimeTolerance))
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

    /// <summary>Observes direct child BuildHosts without terminating or modifying them.</summary>
    internal static UnregisteredBuildHostReapPlan CaptureChildProcesses()
    {
        using var self = Process.GetCurrentProcess();
        return Select(Capture(), NoProcessIds, self.Id, self.StartTime, StartTimeTolerance);
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
                    if (TryObserve(process, out var observation))
                    {
                        map.TryAdd(observation.Id, observation);
                    }
                }
            }
        }

        return map;
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

    // Windows keeps the parent id after the parent exits, so a process started before this one with the
    // same parent id belongs to an earlier process that had this id.
    private static bool IsChild(
        BuildHostProcessObservation process,
        int ownProcessId,
        DateTime ownStartTime,
        TimeSpan startTimeTolerance)
        => process.ParentId == ownProcessId && process.StartTime >= ownStartTime - startTimeTolerance;

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

    private static readonly string[] CandidateProcessNames =
    [
        "dotnet",
        FrameworkHostProcessName,
        "mono",
    ];

    private static readonly HashSet<int> NoProcessIds = [];

    // Windows reports start time exactly. On Linux Process.StartTime is recomputed from a 10 ms tick and can
    // move by a few milliseconds between reads; 100 ms covers that.
    private static readonly TimeSpan StartTimeTolerance = OperatingSystem.IsWindows()
        ? TimeSpan.Zero
        : TimeSpan.FromMilliseconds(100);
}
