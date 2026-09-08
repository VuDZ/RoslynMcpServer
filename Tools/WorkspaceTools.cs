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
        + "briefOutput=true collapses MSBuild/NuGet warnings to category and code counts (default false keeps full messages). Failures always print in full.")]
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

        return ToolTelemetry.TraceAndReturn(nameof(LoadWorkspace), sb.ToString());
    }

    [McpServerTool(Name = "reset_workspace", Title = "Reset C# workspace")]
    [Description("Disposes the in-process workspace cache. Use after building so the next load_workspace picks up generated files. Does not restart the MCP process.")]
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
