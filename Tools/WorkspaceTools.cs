using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

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
            .Select(d => $"{d.Kind}: {d.Message}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (projectCount == 0 || diagnostics.Any(static d => d.Contains("error", StringComparison.OrdinalIgnoreCase)))
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(LoadWorkspace),
                BuildFailureReport(
                    workspacePath,
                    diagnostics.Count > 0 ? diagnostics : new[] { "Workspace loaded with zero projects." }));
        }

        var sb = new StringBuilder();
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
