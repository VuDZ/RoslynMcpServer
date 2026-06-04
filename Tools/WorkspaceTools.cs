using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class WorkspaceTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<WorkspaceTools> _logger;

    public WorkspaceTools(SolutionManager solutionManager, ILogger<WorkspaceTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "load_workspace", Title = "Load C# workspace")]
    [Description("Loads a C# Solution or Project into the semantic engine and returns a structural map of the codebase. Always call this first before analyzing C# code.")]
    public async Task<string> LoadWorkspace(
        [Description("Absolute path to the .sln or .csproj — same parameter name as run_dotnet_build, run_dotnet_test, run_format, list_projects.")]
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        Solution solution;
        try
        {
            solution = await _solutionManager.LoadAsync(workspacePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace from {Path}", workspacePath);
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
            return ToolTelemetry.TraceAndReturn(
                nameof(LoadWorkspace),
                BuildFailureReport(
                    workspacePath,
                    diagnostics.Count > 0 ? diagnostics : new[] { "Workspace loaded with zero projects." }));
        }

        var sb = new StringBuilder();
        sb.AppendLine(WorkspaceHealthReporter.BuildHealthSection(workspacePath, solution));
        sb.AppendLine();
        sb.AppendLine($"Successfully loaded workspace. Found {projectCount} projects:");
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- {project.Name} [{InferCompactProjectType(project)}]");
        }

        if (diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Workspace diagnostics:");
            foreach (var diagnostic in diagnostics)
            {
                sb.AppendLine($"- {diagnostic}");
            }

            if (diagnostics.Any(static d =>
                    d.Contains("do not have a version specified", StringComparison.OrdinalIgnoreCase)))
            {
                sb.AppendLine();
                sb.AppendLine(
                    "> **Note:** Design-time MSBuild can report missing `PackageReference` versions before `dotnet restore`, "
                    + "even when `Version=` is present in the `.csproj` on disk. Run `dotnet restore` at the solution root, "
                    + "then `reset_workspace` and `load_workspace`. Set MCP env `ROSLYN_MCP_WORKSPACE` to the repo root "
                    + "(where `global.json` lives) so MSBuild.Locator pins the same SDK as `run_dotnet_build`.");
            }

            if (diagnostics.Any(static d =>
                    d.Contains("NuGet audit", StringComparison.OrdinalIgnoreCase)))
            {
                sb.AppendLine();
                sb.AppendLine(
                    "> **Note:** NuGet audit advisories (GHSA / NU1903) are shown as warnings here; `dotnet build` may still fail with `NU1904` if audit is treated as error. Use `run_dotnet_build` for the exact NU lines.");
            }
        }

        return ToolTelemetry.TraceAndReturn(nameof(LoadWorkspace), sb.ToString());
    }

    [McpServerTool(Name = "reset_workspace", Title = "Reset C# workspace")]
    [Description(
        "Disposes the in-process MSBuildWorkspace and drops the cached solution. Use after building the loaded solution/project on disk so the next load_workspace picks up fresh references and generated files. Does not restart the MCP process — use stop_mcp_server if the server binary itself was rebuilt.")]
    public async Task<string> ResetWorkspace(CancellationToken cancellationToken = default)
    {
        try
        {
            await _solutionManager.ClearWorkspaceAsync(cancellationToken);
            return ToolTelemetry.TraceAndReturn(
                nameof(ResetWorkspace),
                "Workspace cleared. Call load_workspace again with your .sln or .csproj path.");
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
