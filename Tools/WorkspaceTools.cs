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
    [Description(
        "Loads a C# Solution or Project into the semantic engine and returns a structural map plus a **workspace health** block "
        + "(SDK/global.json pin, restore assets, registered tool count). Always call this first before analyzing C# code. "
        + "Accepts `.sln`, `.slnx`, or `.csproj` (prefer `.sln`/`.slnx` for multi-config solutions so project configurations resolve correctly). "
        + "Large solutions can take minutes — if the host aborts mid-load the tool returns **Workspace Load Cancelled (client abort)** "
        + "(not MSBuild failure); raise host MCP timeout (e.g. OpenCode `timeout: 600000`) and retry. "
        + "NuGet restore warnings (NU1701 TFM compat, audit, unused-package prune) are warnings and do not fail load; "
        + "true MSBuild/SDK errors and unloadable projects still fail. "
        + "Optional `configuration` / `platform` are passed as MSBuildWorkspace global properties (same names VS uses for the active solution config). "
        + "Optional `targetFramework` is the MSBuild `TargetFramework` global property (same idea as `dotnet build -f`). "
        + "Required when `Directory.Build.props` (or the csproj) sets `TargetFrameworks` — the CrossTargeting outer evaluation has no `Compile` target and load fails with **missing Compile target**; pick one inner TFM (e.g. `net10.0`). "
        + "`run_dotnet_build` / `run_dotnet_test` inherit configuration/platform when their own args are omitted.")]
    public async Task<string> LoadWorkspace(
        [Description("Absolute path to a `.sln`, `.slnx`, or `.csproj` file (not a directory). Same parameter name as run_dotnet_build, run_dotnet_test, run_format, list_projects.")]
        string workspacePath,
        [Description(
            "Optional MSBuild Configuration global property (e.g. `Debug`, `Release`, `Sit-Debug`, `kart`). "
            + "Omit for SDK/solution default (typically Debug). Required when TargetFramework is gated on the IDE solution config.")]
        string? configuration = null,
        [Description(
            "Optional MSBuild Platform global property (e.g. `AnyCPU`, `x64`). `Any CPU` is normalized to `AnyCPU`. "
            + "Omit for SDK/solution default.")]
        string? platform = null,
        [Description(
            "Optional MSBuild TargetFramework global property (e.g. `net10.0`, `netstandard2.0`). "
            + "Omit for SDK default. Pass when the solution uses `TargetFrameworks` (multi-targeting / Directory.Build.props) "
            + "so design-time evaluation is an inner TFM that has a `Compile` target. Not inherited by `run_dotnet_build`.")]
        string? targetFramework = null,
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
                targetFramework);
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
                _solutionManager.LoadedConfiguration,
                _solutionManager.LoadedPlatform,
                _solutionManager.LoadedTargetFramework));
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

            if (diagnostics.Any(static d =>
                    d.Contains("NuGet prune", StringComparison.OrdinalIgnoreCase)))
            {
                sb.AppendLine();
                sb.AppendLine(
                    "> **Note:** NuGet prune / unused `PackageReference` advisories are shown as warnings; the workspace is usable. Remove unused package references if you want a clean restore graph.");
            }

            if (diagnostics.Any(static d =>
                    d.Contains("NuGet compat", StringComparison.OrdinalIgnoreCase)))
            {
                sb.AppendLine();
                sb.AppendLine(
                    "> **Note:** NuGet TFM-compat advisories (`NU1701`, netfx package in a netcore/net10 project) are shown as warnings; "
                    + "`dotnet build` / Visual Studio usually succeed. Use `run_dotnet_build` for the exact NU lines. "
                    + "A mis-targeted project may still have incomplete references in Roslyn — prefer fixing the TFM or package.");
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
