using System.Globalization;
using RoslynMcpServer.Services.Workspace.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Switches the optimizations of the forked BuildHost that this server ships in BuildHost-netcore: part A builds the
/// projects a project references before it, part B evaluates projects ahead on worker threads. Both keep the loaded
/// workspace identical to the package's BuildHost and shortened the load by 16-27 % against the fork without them
/// (docs/workspace-load-performance/research-buildhost-fork/evidence/task-02-optimization-spike.md). Candidate C lets
/// the evaluations of one project share one SDK resolution cache instead of loading the workload manifests again for
/// each; on top of A and B it shortened the load by another 10-18 % with the same workspace and host response
/// (docs/workspace-load-performance/research-buildhost-fork/evidence/task-04-projectfileinfo-extraction.md).
/// </summary>
/// <remarks>
/// The switches go to the BuildHost as global properties of the workspace, which the MSBuildWorkspace client passes
/// to its BuildHost and which the fork takes out before any evaluation. The fork also reads environment variables,
/// but they are process-wide: every workspace in the process, including those of tests that study the package's
/// BuildHost, would get them. The properties win over the variables.
/// </remarks>
internal static class BuildHostOptimizations
{
    /// <summary>Environment variable that turns all optimizations off when set to 0 or false.</summary>
    public const string EnabledEnvironmentVariableName = "ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS";

    /// <summary>Environment variable with the worker threads of part B; 0 turns only part B off.</summary>
    public const string WorkersEnvironmentVariableName = "ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS";

    /// <summary>The worker count measured in task-02 on 6 and 16 logical processors; not tuned yet.</summary>
    public const int DefaultEvaluateAheadWorkers = 4;

    /// <summary>
    /// The global property in which the fork learns which solution the client loads, so part B can evaluate the
    /// solution entries that no other project references. The fork takes it out of the global properties before any
    /// evaluation; the package's .NET Framework BuildHost keeps it as a property no project reads.
    /// </summary>
    public const string SolutionPathProperty = "RoslynMcpSolutionPath";

    /// <summary>The fork's switch of part A: 1 on, 0 off.</summary>
    public const string ReferencesFirstProperty = "RoslynMcpReferencesFirst";

    /// <summary>The fork's switch of part B: the worker threads, 0 off.</summary>
    public const string EvaluateAheadProperty = "RoslynMcpEvaluateAhead";

    /// <summary>The fork's switch of candidate C, the shared SDK resolution cache: 1 on, 0 off.</summary>
    public const string SharedSdkCacheProperty = "RoslynMcpSharedSdkCache";

    /// <summary>
    /// The environment variables win over the RoslynMcp.jsonc keys <c>buildhost-optimizations</c> and
    /// <c>buildhost-evaluate-ahead-workers</c>, as for search-prewarm; with neither set all optimizations are on, with
    /// <see cref="DefaultEvaluateAheadWorkers"/> workers. A worker count that is not a non-negative integer is ignored.
    /// </summary>
    public static BuildHostOptimizationSettings Resolve(bool? fileEnabled, int? fileWorkers) =>
        Resolve(
            fileEnabled,
            fileWorkers,
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariableName),
            Environment.GetEnvironmentVariable(WorkersEnvironmentVariableName));

    internal static BuildHostOptimizationSettings Resolve(bool? fileEnabled, int? fileWorkers, string? enabledValue, string? workersValue)
    {
        var enabled = string.IsNullOrWhiteSpace(enabledValue)
            ? fileEnabled ?? true
            : !(enabledValue.Trim() == "0" || string.Equals(enabledValue.Trim(), "false", StringComparison.OrdinalIgnoreCase));
        if (!enabled)
        {
            return new BuildHostOptimizationSettings(ReferencesFirst: false, EvaluateAheadWorkers: 0, SharedSdkCache: false);
        }

        var workers = int.TryParse(workersValue?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fileWorkers ?? DefaultEvaluateAheadWorkers;
        return new BuildHostOptimizationSettings(ReferencesFirst: true, EvaluateAheadWorkers: workers, SharedSdkCache: true);
    }

    /// <summary>
    /// The global properties for <c>MSBuildWorkspace.Create</c>: <paramref name="properties"/>, the switches of
    /// <paramref name="settings"/> and, when <paramref name="fullPath"/> is a solution or a solution filter,
    /// <see cref="SolutionPathProperty"/>. The requested properties stay as they are for everything else, such as the
    /// analyzer provenance snapshot and the load cache.
    /// </summary>
    public static Dictionary<string, string> ForWorkspace(
        IReadOnlyDictionary<string, string> properties,
        string fullPath,
        BuildHostOptimizationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var result = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)
        {
            [ReferencesFirstProperty] = settings.ReferencesFirst ? "1" : "0",
            [EvaluateAheadProperty] = settings.EvaluateAheadWorkers.ToString(CultureInfo.InvariantCulture),
            [SharedSdkCacheProperty] = settings.SharedSdkCache ? "1" : "0",
        };
        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnf", StringComparison.OrdinalIgnoreCase))
        {
            result[SolutionPathProperty] = Path.GetFullPath(fullPath);
        }

        return result;
    }
}
