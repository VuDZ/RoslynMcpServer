using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class ProjectTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<ProjectTools> _logger;

    public ProjectTools(SolutionManager solutionManager, ILogger<ProjectTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "rename_project", Title = "Rename MSBuild project")]
    [Description(
        "Renames an SDK-style project folder and `.csproj`, updates `AssemblyName`/`RootNamespace` when they match the old project name, " +
        "fixes `ProjectReference` paths, and updates `.sln`/`.slnx` entries. Does **not** rename C# namespaces/types (use `rename_symbol` after reload). " +
        "Does not touch Docker, launchSettings, CI, or docs. Requires the project to live in its own identically named folder. Prefer `dryRun=true` first.")]
    public Task<string> RenameProject(
        [Description("Absolute or workspace-relative path to the `.csproj` to rename.")] string projectPath,
        [Description("New project name (single path segment), e.g. `DupFinder.Core`.")] string newProjectName,
        [Description("When true (default), returns the planned moves/edits without writing.")] bool dryRun = true,
        [Description("Optional root to search for sibling `.csproj` / `.sln` / `.slnx`. Defaults to `ROSLYN_MCP_WORKSPACE` or nearest solution parent.")] string? searchRoot = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RenameProject);
        _ = cancellationToken;

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(newProjectName))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Error: `projectPath` and `newProjectName` are required."));
            }

            var resolvedProject = _solutionManager.ResolvePathAgainstWorkspace(projectPath);
            var resolvedSearchRoot = string.IsNullOrWhiteSpace(searchRoot)
                ? null
                : _solutionManager.ResolvePathAgainstWorkspace(searchRoot);

            var plan = ProjectRenameHelper.CreatePlan(resolvedProject, newProjectName, resolvedSearchRoot);
            if (dryRun)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(toolName, ProjectRenameHelper.FormatPlan(plan, dryRun: true)));
            }

            var result = ProjectRenameHelper.Apply(plan);
            return ClearWorkspaceAndReturnAsync(toolName, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenameProject failed for {ProjectPath} -> {NewName}", projectPath, newProjectName);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "add_package_reference", Title = "Add NuGet package reference")]
    [Description(
        "Adds a PackageReference to a .csproj file. Verify package id/version with search_nuget_registry first. "
        + "Relative `projectPath` resolves against process CWD (prefer absolute paths). Clears in-memory workspace — call `load_workspace` after.")]
    public async Task<string> AddPackageReference(
        [Description("Path to `.csproj` (prefer absolute; relative uses process CWD).")] string projectPath,
        [Description("NuGet package id, e.g. Moq.")] string packageId,
        [Description("Optional version. Omit to add without Version attribute.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(AddPackageReference);
        try
        {
            var message = await ProjectFileHelper.AddPackageReferenceAsync(projectPath, packageId, version, cancellationToken)
                .ConfigureAwait(false);
            await _solutionManager.ClearWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            return ToolTelemetry.TraceAndReturn(toolName, message + " Call `load_workspace` to refresh Roslyn state.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddPackageReference failed for {PackageId}", packageId);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "remove_package_reference", Title = "Remove NuGet package reference")]
    [Description(
        "Removes a PackageReference from a .csproj file. Relative `projectPath` resolves against process CWD (prefer absolute). "
        + "Clears in-memory workspace — call `load_workspace` after.")]
    public async Task<string> RemovePackageReference(
        [Description("Path to `.csproj` (prefer absolute; relative uses process CWD).")]
        string projectPath,
        [Description("NuGet package id to remove, e.g. Moq.")]
        string packageId,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RemovePackageReference);
        try
        {
            var message = await ProjectFileHelper.RemovePackageReferenceAsync(projectPath, packageId, cancellationToken)
                .ConfigureAwait(false);
            await _solutionManager.ClearWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            return ToolTelemetry.TraceAndReturn(toolName, message + " Call `load_workspace` to refresh Roslyn state.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemovePackageReference failed for {PackageId}", packageId);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    private async Task<string> ClearWorkspaceAndReturnAsync(string toolName, string message)
    {
        try
        {
            await _solutionManager.ClearWorkspaceAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ClearWorkspace after RenameProject failed");
        }

        return ToolTelemetry.TraceAndReturn(
            toolName,
            message + Environment.NewLine + Environment.NewLine
            + "Workspace cache cleared. Call `load_workspace` on the solution, then `run_dotnet_build`.");
    }
}
