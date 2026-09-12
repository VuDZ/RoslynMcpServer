using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class WorkspaceTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<WorkspaceTools> _logger;
    private readonly McpToolActivationService _activation;

    public WorkspaceTools(SolutionManager solutionManager, ILogger<WorkspaceTools> logger, McpToolActivationService activation)
    {
        _solutionManager = solutionManager;
        _logger = logger;
        _activation = activation;
    }

    [McpServerTool(Name = "load_workspace", Title = "Load C# workspace")]
    [Description(
        "Loads a .sln, .slnx, or .csproj into the semantic workspace. Call this first before C# analysis. "
        + "Optional configuration/platform/targetFramework are MSBuild global properties; targetFramework is required when the project uses TargetFrameworks. "
        + "Optional buildArgs is a session suffix for later `dotnet build` (probe and pre-test build); do not put -c / -p:Platform / -v / --no-incremental there. "
        + "briefOutput=true collapses MSBuild/NuGet warnings to category and code counts (default false keeps full messages). Failures always print in full. "
        + "logProjectOutputDiagnostics=true logs per-project OutputFilePath/GeneratedFilesOutputDirectory and AnalyzerReference file existence/timestamp to the MCP server log (diagnostic-only, not returned in this response) — use to debug analyzer/generator projects not producing generated sources when Directory.Build.props overrides OutputPath. "
        + "shadowCopyInSolutionAnalyzers=true fixes that same case: rewrites AnalyzerReferences that point at another in-solution project's build output to a private shadow copy of that project's own resolved output, so source generation works even when the design-time-resolved AnalyzerReference path was wrong, and the real build output is never locked by this process. Requires the referenced analyzer/generator project to have been built at least once. Prepared generations are content-hashed and immutable; later document edits reapply the in-memory mapping without recopying analyzer files. Same-identity rebuild needs a new MCP process (reset_workspace does not unload CLR); private helper DLLs are refused (main-only).")]
    public async Task<string> LoadWorkspace(
        [Description("Path to a .sln, .slnx, or .csproj file, not a directory.")]
        string workspacePath,
        [Description("MSBuild Configuration. Omit for SDK default.")]
        string? configuration = null,
        [Description("MSBuild Platform. Any CPU is normalized to AnyCPU.")]
        string? platform = null,
        [Description("MSBuild TargetFramework. Required for multi-targeting. Not inherited by run_dotnet_build.")]
        string? targetFramework = null,
        [Description("Extra arguments appended to later `dotnet build` (probe and pre-test build). Omit for none. Do not include -c, -p:Platform, -v, or --no-incremental.")]
        string? buildArgs = null,
        [Description("When true, collapse workspace warnings to category and code counts. Default false keeps full messages. Failures always print in full.")]
        bool briefOutput = false,
        [Description("When true, log per-project OutputFilePath/GeneratedFilesOutputDirectory and AnalyzerReference existence/timestamp at Information level (see tail_tool_log / read_log_tail). Default false. Diagnostic-only; not included in this tool's return value.")]
        bool logProjectOutputDiagnostics = false,
        [Description("When true, rewrite AnalyzerReferences pointing at another in-solution project's build output to a shadow copy of that project's own resolved output (fixes source generation broken by a Directory.Build.props OutputPath override, and avoids locking the real build output). Default false. Requires the referenced project to already have a build output on disk. Generations are content-hashed and reused on document edit without recopying. Same-identity refresh requires restarting the MCP process; reset_workspace does not unload CLR assemblies; private helper DLLs are refused. A short summary is included in this response; details go to the MCP server log.")]
        bool shadowCopyInSolutionAnalyzers = false,
        CancellationToken cancellationToken = default)
    {
        Solution solution;
        try
        {
            solution = await _solutionManager.LoadAsync(
                workspacePath,
                cancellationToken,
                configuration,
                platform,
                targetFramework,
                buildArgs);
        }
        catch (ArgumentException ex)
        {
            return ToolTelemetry.TraceAndReturn(nameof(LoadWorkspace), $"Error: {ex.Message}");
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "load_workspace cancelled by client for {Path}", workspacePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(LoadWorkspace),
                WorkspaceLoadGuidance.FormatClientCancelledWorkspaceLoadMessage(workspacePath)
                + Environment.NewLine
                + MsBuildEnvironmentInfo.FormatMarkdownSection());
        }
        catch (RoslynMsBuildBuildHostException ex)
        {
            _logger.LogError(ex, "Failed to load workspace from {Path} (VS 2026 / MSBuild 18 BuildHost)", workspacePath);
            return ToolTelemetry.TraceAndReturn(nameof(LoadWorkspace), ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace from {Path}", workspacePath);
            if (WorkspaceLoadGuidance.IsRoslynMsBuildBuildHostFailure(ex))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(LoadWorkspace),
                    WorkspaceLoadGuidance.FormatRoslynMsBuildBuildHostFailureMessage(workspacePath));
            }

            return ToolTelemetry.TraceAndReturn(nameof(LoadWorkspace), BuildFailureReport(workspacePath, new[] { ex.Message }));
        }

        var projects = solution.Projects.ToList();
        var projectCount = projects.Count;
        var diagnostics = _solutionManager.LastDiagnostics
            .Select(d => WorkspaceDiagnosticFormatter.Format(d.Kind.ToString(), d.Message))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (projectCount == 0 || diagnostics.Any(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure))
        {
            if (diagnostics.Any(WorkspaceDiagnosticFormatter.IsMissingTargetFrameworkEvaluation))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(LoadWorkspace),
                    WorkspaceLoadGuidance.FormatMissingTargetFrameworkWorkspaceLoadMessage(
                        workspacePath,
                        diagnostics,
                        _solutionManager.LoadedConfiguration,
                        _solutionManager.LoadedPlatform));
            }

            if (diagnostics.Any(WorkspaceDiagnosticFormatter.IsMissingCompileTarget))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(LoadWorkspace),
                    WorkspaceLoadGuidance.FormatMissingCompileTargetWorkspaceLoadMessage(
                        workspacePath,
                        diagnostics,
                        _solutionManager.LoadedConfiguration,
                        _solutionManager.LoadedPlatform,
                        _solutionManager.LoadedTargetFramework));
            }

            if (diagnostics.Any(WorkspaceLoadGuidance.IsRoslynMsBuildBuildHostFailure))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(LoadWorkspace),
                    WorkspaceLoadGuidance.FormatRoslynMsBuildBuildHostFailureMessage(workspacePath));
            }

            return ToolTelemetry.TraceAndReturn(
                nameof(LoadWorkspace),
                BuildFailureReport(
                    workspacePath,
                    diagnostics.Count > 0 ? diagnostics : new[] { "Workspace loaded with zero projects." }));
        }

        if (logProjectOutputDiagnostics)
        {
            try
            {
                ProjectOutputDiagnosticsLogger.Log(solution, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "logProjectOutputDiagnostics logging failed for {Path}", workspacePath);
            }
        }

        string? shadowCopySummary = null;
        if (shadowCopyInSolutionAnalyzers)
        {
            try
            {
                var results = await _solutionManager.ShadowCopyInSolutionAnalyzerReferencesAsync(cancellationToken);
                shadowCopySummary = FormatShadowCopySummary(results, _solutionManager.LastExecutionObservation);
                LogShadowCopyResults(results);
                solution = _solutionManager.GetCurrentSolution() ?? solution;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "shadowCopyInSolutionAnalyzers failed for {Path}", workspacePath);
                shadowCopySummary = $"- **Analyzer reference shadow copy:** failed — {ex.Message}";
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            WorkspaceHealthReporter.BuildHealthSection(
                workspacePath,
                solution,
                _activation,
                _solutionManager.LoadedConfiguration,
                _solutionManager.LoadedPlatform,
                _solutionManager.LoadedTargetFramework,
                _solutionManager.LoadedBuildArgs));
        sb.AppendLine();
        sb.AppendLine($"Successfully loaded workspace. Found {projectCount} projects:");
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- {project.Name} [{InferCompactProjectType(project)}]");
        }

        var diagnosticSection = WorkspaceLoadDiagnosticsReporter.FormatSection(diagnostics, briefOutput);
        if (!string.IsNullOrEmpty(diagnosticSection))
        {
            sb.AppendLine();
            sb.AppendLine(diagnosticSection);
        }

        if (shadowCopySummary is not null)
        {
            sb.AppendLine();
            sb.AppendLine(shadowCopySummary);
        }

        return ToolTelemetry.TraceAndReturn(nameof(LoadWorkspace), sb.ToString());
    }

    private static string FormatShadowCopySummary(
        IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> results,
        AnalyzerExecutionObservation execution)
    {
        var sb = new StringBuilder();
        if (results.Count == 0)
        {
            sb.Append("- **Analyzer reference shadow copy:** no in-solution analyzer candidates; nothing rewritten.");
        }
        else
        {
            var applied = results.Count(r => r.Applied);
            var skipped = results.Count - applied;
            sb.Append("- **Analyzer reference shadow copy:** ").Append(applied).Append(" rewritten");
            if (skipped > 0)
            {
                sb.Append(", ").Append(skipped).Append(" skipped (see tail_tool_log for reasons)");
            }

            var stale = results.Count(r => r.StaleGeneration);
            if (stale > 0)
            {
                sb.Append(" (").Append(stale).Append(" stale-generation)");
            }

            sb.Append('.');
        }

        if (execution.Status is not AnalyzerExecutionStatus.None)
        {
            sb.AppendLine();
            sb.Append("- **Analyzer execution:** ").Append(execution.Status);
            if (!string.IsNullOrWhiteSpace(execution.Reason))
            {
                sb.Append(" — ").Append(execution.Reason);
            }

            if (!string.IsNullOrWhiteSpace(execution.Action))
            {
                sb.Append(" Action: ").Append(execution.Action).Append('.');
            }
        }

        return sb.ToString();
    }

    private void LogShadowCopyResults(IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> results)
    {
        foreach (var result in results)
        {
            if (result.Applied)
            {
                _logger.LogInformation(
                    "AnalyzerReferenceShadowCopy project={ProjectName} selectedProject={SelectedProjectName} reasonCode={ReasonCode} selectionBasis={SelectionBasis} originalFullPath={OriginalFullPath} originalPathState={OriginalPathState} selectedSourcePath={SelectedSourcePath} selectedSourcePathState={SelectedSourcePathState} shadowCopyPath={ShadowCopyPath} generation={GenerationId}",
                    result.ProjectName,
                    result.MatchedProjectName ?? "(none)",
                    result.ReasonCode,
                    result.SelectionBasis,
                    result.OriginalFullPath ?? "(null)",
                    result.OriginalPathState,
                    result.SelectedSourcePath ?? "(none)",
                    result.SelectedSourcePathState,
                    result.ShadowCopyPath,
                    result.GenerationId ?? "(none)");
            }
            else
            {
                _logger.LogWarning(
                    "AnalyzerReferenceShadowCopy project={ProjectName} selectedProject={SelectedProjectName} reasonCode={ReasonCode} selectionBasis={SelectionBasis} originalFullPath={OriginalFullPath} originalPathState={OriginalPathState} selectedSourcePath={SelectedSourcePath} selectedSourcePathState={SelectedSourcePathState} generation={GenerationId} skipReason={SkipReason}",
                    result.ProjectName,
                    result.MatchedProjectName ?? "(none)",
                    result.ReasonCode,
                    result.SelectionBasis,
                    result.OriginalFullPath ?? "(null)",
                    result.OriginalPathState,
                    result.SelectedSourcePath ?? "(none)",
                    result.SelectedSourcePathState,
                    result.GenerationId ?? "(none)",
                    result.SkipReason);
            }
        }
    }

    [McpServerTool(Name = "reset_workspace", Title = "Reset C# workspace")]
    [Description("Disposes the in-process workspace cache. Use after building so the next load_workspace picks up generated files. Does not restart the MCP process and does not delete published analyzer shadow generations.")]
    public async Task<string> ResetWorkspace(CancellationToken cancellationToken = default)
    {
        try
        {
            await _solutionManager.ClearWorkspaceAsync(cancellationToken);
            return ToolTelemetry.TraceAndReturn(
                nameof(ResetWorkspace),
                "Workspace cleared. Call load_workspace again with your .sln, .slnx, or .csproj path.");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(ResetWorkspace), "Reset was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResetWorkspace failed");
            return ToolTelemetry.TraceAndReturn(nameof(ResetWorkspace), $"Failed to reset workspace: {ex.Message}");
        }
    }

    private static string BuildFailureReport(string path, IEnumerable<string> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Workspace Load Failed");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{path}`");
        sb.AppendLine();
        sb.AppendLine("### Errors");
        foreach (var error in errors)
        {
            sb.AppendLine($"- {error}");
        }

        sb.Append(MsBuildEnvironmentInfo.FormatMarkdownSection());
        return sb.ToString();
    }

    private static string InferCompactProjectType(Project project)
    {
        var references = project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Select(r => r.Display ?? string.Empty)
            .Where(static d => !string.IsNullOrWhiteSpace(d))
            .ToArray();

        if (ContainsAny(references, "xunit", "nunit", "mstest", "microsoft.net.test.sdk")
            || ContainsAny(project.AssemblyName, ".tests", "tests"))
        {
            return "Test";
        }

        if (ContainsAny(references, "microsoft.aspnetcore.app"))
        {
            return "Web API";
        }

        if (ContainsAny(references, "microsoft.extensions.hosting"))
        {
            return "Worker";
        }

        return "Library";
    }

    private static bool ContainsAny(IEnumerable<string> values, params string[] markers)
    {
        foreach (var value in values)
        {
            if (ContainsAny(value, markers))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string? value, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
